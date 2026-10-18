using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EVCharging.Api.Ocpp;
using EVCharging.Infrastructure.Hosting;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OcppReconnectTests
{
    [Fact]
    public void RepeatedReleaseOfOldOwnerCannotRemoveReplacement()
    {
        var registry = new OcppConnectionRegistry();
        var deviceId = Guid.NewGuid();
        using var first = registry.TryOpen(deviceId);
        Assert.NotNull(first);
        Assert.Null(registry.TryOpen(deviceId));
        first.Dispose();
        using var second = registry.TryOpen(deviceId);
        Assert.NotNull(second);
        first.Dispose();
        Assert.Null(registry.TryOpen(deviceId));
        second.Dispose();
        using var third = registry.TryOpen(deviceId);
        Assert.NotNull(third);
    }

    [Theory]
    [InlineData("peer_close", WebSocketCloseStatus.NormalClosure)]
    [InlineData("invalid_frame_type_or_size", WebSocketCloseStatus.MessageTooBig)]
    [InlineData("connection_permission_revoked", WebSocketCloseStatus.PolicyViolation)]
    [InlineData("invalid_payload", WebSocketCloseStatus.InvalidPayloadData)]
    [InlineData("processing_failed", WebSocketCloseStatus.InternalServerError)]
    [InlineData("transport_error", WebSocketCloseStatus.InvalidPayloadData)]
    [InlineData("idle_or_processing_timeout", WebSocketCloseStatus.InvalidPayloadData)]
    public async Task DisconnectCommitsBeforeCloseAndReconnectSurvivesOldFinally(
        string reason,
        WebSocketCloseStatus expectedCode
    )
    {
        var persistence = new DisconnectPersistence { PauseFirstClose = true };
        await using var host = new EndpointHost(persistence);
        var old = host.Start();
        await old.Socket.Accepted.Task.WaitAsync(host.Token);
        var original = Assert.Single(await host.ConnectionsAsync());
        await host.TriggerAsync(old.Socket, reason);

        await persistence.Closing.Task.WaitAsync(host.Token);
        Assert.False(old.Socket.CloseSent.Task.IsCompleted);
        Assert.Null(Assert.Single(await host.ConnectionsAsync()).DisconnectedAt);
        await host.AssertDuplicateAsync();

        persistence.Continue.TrySetResult();
        Assert.Equal(expectedCode, await old.Socket.CloseSent.Task.WaitAsync(host.Token));
        var closed = Assert.Single(await host.ConnectionsAsync());
        Assert.NotNull(closed.DisconnectedAt);
        Assert.Equal(reason, closed.CloseReason);
        Assert.Equal((int)expectedCode, closed.CloseCode);
        Assert.False(old.Completion.IsCompleted);

        // Hold the old CloseOutputAsync after the peer can observe Close. Reconnect
        // without allowing its finally to run, then finish that old request explicitly.
        var replacement = host.Start();
        await replacement.Socket.Accepted.Task.WaitAsync(host.Token);
        var current = Assert.Single(await host.ConnectionsAsync(), x => x.DisconnectedAt is null);
        Assert.NotEqual(original.Id, current.Id);
        Assert.Equal(original.ChargePointId, current.ChargePointId);
        await host.HeartbeatAsync(replacement.Socket);
        await host.AssertDuplicateAsync();
        old.Socket.ContinueClose.TrySetResult();
        await old.Completion.WaitAsync(host.Token);
        await host.AssertDuplicateAsync();
        await host.HeartbeatAsync(replacement.Socket);

        var connections = await host.ConnectionsAsync();
        Assert.Equal(2, connections.Length);
        Assert.Null(Assert.Single(connections, x => x.Id == current.Id).DisconnectedAt);
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var device = await db.OcppChargePoints.SingleAsync(host.Token);
        Assert.NotNull(device.LastHeartbeatAt);
        Assert.NotNull(device.LastSeenAt);
        Assert.Equal(
            2,
            await db.OcppInboundMessages.CountAsync(
                x => x.ConnectionId == current.Id && x.Action == "Heartbeat",
                host.Token
            )
        );
        Assert.Equal(1, persistence.CloseAttempts);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("accept")]
    [InlineData("persist_once")]
    [InlineData("persist_always")]
    [InlineData("close_output")]
    [InlineData("dispose")]
    public async Task ExceptionsAlwaysReleaseOwnershipAndPermitRecovery(string failure)
    {
        var persistence = new DisconnectPersistence
        {
            FailOpen = failure == "open",
            FailedClosesRemaining = failure switch
            {
                "persist_once" => 1,
                "persist_always" => int.MaxValue,
                _ => 0,
            },
        };
        await using var host = new EndpointHost(persistence);
        var old = host.Start(failure);
        if (failure is not ("open" or "accept"))
        {
            await old.Socket.Accepted.Task.WaitAsync(host.Token);
            old.Socket.ContinueClose.TrySetResult();
            await host.TriggerAsync(old.Socket, "peer_close");
        }
        if (failure is "open" or "accept" or "persist_always" or "dispose")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                old.Completion.WaitAsync(host.Token)
            );
        else
            await old.Completion.WaitAsync(host.Token);

        if (failure == "persist_always")
        {
            Assert.False(old.Socket.CloseSent.Task.IsCompleted);
            Assert.Null(Assert.Single(await host.ConnectionsAsync()).DisconnectedAt);
        }
        else if (failure != "open")
            Assert.NotNull(Assert.Single(await host.ConnectionsAsync()).DisconnectedAt);

        persistence.FailOpen = false;
        persistence.FailedClosesRemaining = 0;
        var replacement = host.Start();
        await replacement.Socket.Accepted.Task.WaitAsync(host.Token);
        await host.HeartbeatAsync(replacement.Socket);
        await host.AssertDuplicateAsync();
        Assert.Single(await host.ConnectionsAsync(), x => x.DisconnectedAt is null);
    }

    private sealed class DisconnectPersistence : SaveChangesInterceptor
    {
        public bool PauseFirstClose { get; init; }
        public bool FailOpen { get; set; }
        public int FailedClosesRemaining { get; set; }
        public int CloseAttempts { get; private set; }
        public TaskCompletionSource Closing { get; } = NewSignal();
        public TaskCompletionSource Continue { get; } = NewSignal();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var entries = data.Context!.ChangeTracker.Entries<OcppConnection>().ToArray();
            if (FailOpen && entries.Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Controlled open persistence failure.");
            if (
                entries.Any(x =>
                    x.State == EntityState.Modified && x.Entity.DisconnectedAt is not null
                )
            )
            {
                CloseAttempts++;
                Closing.TrySetResult();
                if (PauseFirstClose && CloseAttempts == 1)
                    await Continue.Task.WaitAsync(cancellationToken);
                if (FailedClosesRemaining > 0)
                {
                    FailedClosesRemaining--;
                    throw new InvalidOperationException(
                        "Controlled disconnect persistence failure."
                    );
                }
            }
            return result;
        }
    }

    // Invoke the registered endpoint with real scoped gateways and InMemory storage.
    // Only the transport is controlled; no listener, tunnel, or external DB is used.
    private sealed class EndpointHost : IAsyncDisposable
    {
        private readonly ApiFactory original;
        private readonly DisconnectPersistence persistence;
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        private readonly List<PendingConnection> requests = [];
        private readonly RouteEndpoint endpoint;
        public WebApplicationFactory<Program> Factory { get; }
        public CancellationToken Token => timeout.Token;

        public EndpointHost(DisconnectPersistence persistence)
        {
            this.persistence = persistence;
            original = new ApiFactory(saveInterceptor: persistence, testMode: false);
            Factory = original.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.PostConfigure<CloudflareTunnelOptions>(options =>
                        options.Mode = "direct"
                    )
                )
            );
            endpoint = Factory
                .Services.GetRequiredService<EndpointDataSource>()
                .Endpoints.OfType<RouteEndpoint>()
                .Single(x => x.RoutePattern.RawText == "/ws/cs/{identity}");
        }

        public PendingConnection Start(string? failure = null)
        {
            var socket = new ControlledSocket(failure);
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/ws/cs/CP-RECONNECT";
            context.Request.Headers.SecWebSocketProtocol = "ocpp1.6";
            context.RequestAborted = Token;
            context.Features.Set<IHttpWebSocketFeature>(socket);
            context.SetEndpoint(endpoint);
            var pending = new PendingConnection(socket, context, RunAsync(context));
            requests.Add(pending);
            return pending;
        }

        private async Task RunAsync(HttpContext context)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            context.RequestServices = scope.ServiceProvider;
            await endpoint.RequestDelegate!(context);
        }

        public async Task AssertDuplicateAsync()
        {
            var duplicate = Start();
            await duplicate.Completion.WaitAsync(Token);
            Assert.Equal(StatusCodes.Status409Conflict, duplicate.Context.Response.StatusCode);
            Assert.False(duplicate.Socket.Accepted.Task.IsCompleted);
        }

        public async Task<OcppConnection[]> ConnectionsAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .OcppConnections.AsNoTracking()
                .ToArrayAsync(Token);
        }

        public async Task TriggerAsync(ControlledSocket socket, string reason)
        {
            if (reason == "connection_permission_revoked")
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
                (await db.OcppChargePoints.SingleAsync(Token)).CredentialVersion++;
                await db.SaveChangesAsync(Token);
            }
            switch (reason)
            {
                case "peer_close":
                    socket.Queue(WebSocketMessageType.Close);
                    break;
                case "invalid_frame_type_or_size":
                    socket.Queue(WebSocketMessageType.Binary);
                    break;
                case "invalid_payload":
                    socket.Queue(WebSocketMessageType.Text, "{");
                    break;
                case "transport_error":
                    socket.QueueError(new WebSocketException("Controlled receive failure."));
                    break;
                case "idle_or_processing_timeout":
                    socket.QueueError(new OperationCanceledException());
                    break;
                default:
                    socket.FailSend = reason == "processing_failed";
                    socket.Queue(WebSocketMessageType.Text, "[2,\"trigger\",\"Heartbeat\",{}]");
                    break;
            }
        }

        public async Task HeartbeatAsync(ControlledSocket socket)
        {
            var callId = Guid.NewGuid().ToString();
            socket.Queue(WebSocketMessageType.Text, $"[2,\"{callId}\",\"Heartbeat\",{{}}]");
            using var reply = JsonDocument.Parse(await socket.Replies.Reader.ReadAsync(Token));
            Assert.Equal(3, reply.RootElement[0].GetInt32());
            Assert.Equal(callId, reply.RootElement[1].GetString());
            Assert.True(
                reply.RootElement[2].GetProperty("currentTime").TryGetDateTimeOffset(out _)
            );
        }

        public async ValueTask DisposeAsync()
        {
            persistence.Continue.TrySetResult();
            persistence.FailOpen = false;
            persistence.FailedClosesRemaining = 0;
            foreach (var request in requests)
            {
                request.Socket.ContinueClose.TrySetResult();
                request.Socket.Queue(WebSocketMessageType.Close);
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                // Exceptions are asserted by the tests; observe every request on cleanup too.
                await Task.WhenAll(requests.Select(x => ObserveAsync(x.Completion)))
                    .WaitAsync(cleanup.Token);
            }
            finally
            {
                timeout.Cancel();
                await Factory.DisposeAsync();
                await original.DisposeAsync();
                timeout.Dispose();
            }
        }

        private static async Task ObserveAsync(Task completion)
        {
            try
            {
                await completion;
            }
            catch (InvalidOperationException) { }
        }
    }

    private sealed record PendingConnection(
        ControlledSocket Socket,
        HttpContext Context,
        Task Completion
    );

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ControlledSocket(string? failure) : WebSocket, IHttpWebSocketFeature
    {
        private readonly Channel<Func<ArraySegment<byte>, WebSocketReceiveResult>> incoming =
            Channel.CreateUnbounded<Func<ArraySegment<byte>, WebSocketReceiveResult>>();
        private WebSocketState state = WebSocketState.Open;
        private WebSocketCloseStatus? closeStatus;
        public TaskCompletionSource Accepted { get; } = NewSignal();
        public TaskCompletionSource<WebSocketCloseStatus> CloseSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueClose { get; } = NewSignal();
        public Channel<byte[]> Replies { get; } = Channel.CreateUnbounded<byte[]>();
        public bool FailSend { get; set; }
        public override WebSocketCloseStatus? CloseStatus => closeStatus;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => state;
        public override string SubProtocol => "ocpp1.6";
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            if (failure == "accept")
                throw new InvalidOperationException("Controlled upgrade failure.");
            Assert.Equal("ocpp1.6", context.SubProtocol);
            Accepted.TrySetResult();
            return Task.FromResult<WebSocket>(this);
        }

        public void Queue(WebSocketMessageType type, string payload = "") =>
            incoming.Writer.TryWrite(buffer =>
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                bytes.AsSpan().CopyTo(buffer.AsSpan());
                if (type == WebSocketMessageType.Close)
                {
                    state = WebSocketState.CloseReceived;
                    closeStatus = WebSocketCloseStatus.NormalClosure;
                }
                return new(bytes.Length, type, true, CloseStatus, null);
            });

        public void QueueError(Exception error) => incoming.Writer.TryWrite(_ => throw error);

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        ) => (await incoming.Reader.ReadAsync(cancellationToken))(buffer);

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken
        ) =>
            FailSend
                ? Task.FromException(
                    new InvalidOperationException("Controlled processing failure.")
                )
                : Replies.Writer.WriteAsync(buffer.ToArray(), cancellationToken).AsTask();

        public override async Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        )
        {
            if (failure == "close_output")
                throw new WebSocketException("Controlled close failure.");
            state =
                State == WebSocketState.CloseReceived
                    ? WebSocketState.Closed
                    : WebSocketState.CloseSent;
            CloseSent.TrySetResult(closeStatus);
            await ContinueClose.Task.WaitAsync(cancellationToken);
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        ) => CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override void Abort() => state = WebSocketState.Aborted;

        public override void Dispose()
        {
            state = WebSocketState.Closed;
            if (failure == "dispose")
                throw new InvalidOperationException("Controlled disposal failure.");
        }
    }
}

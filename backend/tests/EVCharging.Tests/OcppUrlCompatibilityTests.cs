using System.Net;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    private static readonly string[] CompatiblePaths =
    [
        "/ws/cs/CP-URL",
        "/CP-URL",
        "/ws/CP-URL",
        "/ocpp/CP-URL",
        "/cs/CP-URL",
        "/ws/cs/CP-URL/CP-URL",
        "/vendor/ocpp/CP-URL/",
        "/ws/cs/OTHER-ID/CP-URL",
    ];

    [Fact]
    public async Task CompatibleUrlsReuseTheFinalIdentityAndPersistBootHeartbeatAndCorrelatedLogs()
    {
        Guid? deviceId = null;
        foreach (var path in CompatiblePaths)
        {
            using var socket = await SocketPathAsync(path);
            Assert.Equal("ocpp1.6", socket.SubProtocol);
            var boot = await CallAsync(
                socket,
                "BootNotification",
                new { chargePointVendor = "Compatibility", chargePointModel = "Physical" }
            );
            Assert.Equal(3, boot[0].GetInt32());
            Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
            Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = Assert.Single(await db.OcppChargePoints.ToListAsync(timeout.Token));
            Assert.Equal("CP-URL", device.Identity);
            deviceId ??= device.Id;
            Assert.Equal(deviceId.Value, device.Id);
            Assert.NotNull(device.LastHeartbeatAt);
            var connection = await db
                .OcppConnections.OrderByDescending(x => x.ConnectedAt)
                .FirstAsync(timeout.Token);
            Assert.NotNull(connection.DisconnectedAt);
            var entries = factory
                .Services.GetRequiredService<IApplicationDiagnostics>()
                .Read("OCPP")
                .Entries;
            var connected = Assert.Single(
                entries,
                x =>
                    x.Context?.EventName == "websocket_connected"
                    && x.Context.ConnectionId == connection.Id
            );
            Assert.Equal("CP-URL", connected.Identity);
            var handshake = Assert.Single(
                entries,
                x =>
                    x.Context?.EventName == "handshake_received"
                    && x.Context.TraceId == connected.Context!.TraceId
            );
            Assert.Equal("CP-URL", handshake.Identity);
            Assert.Contains(path.TrimEnd('/'), handshake.Message);
            Assert.Contains("routeMatched=True", handshake.Message);
            Assert.Contains(
                entries,
                x =>
                    x.Context?.Action == "Heartbeat"
                    && x.Context.ConnectionId == connection.Id
                    && x.Identity == "CP-URL"
            );
        }
        await using var finalScope = factory.Services.CreateAsyncScope();
        Assert.Equal(
            CompatiblePaths.Length,
            await finalScope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .OcppConnections.CountAsync(timeout.Token)
        );
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ws")]
    [InlineData("/WS")]
    [InlineData("/ocpp/")]
    [InlineData("/ws/cs/")]
    [InlineData("/OCPP/BAD.ID")]
    [InlineData("/ws/cs/BAD:ID")]
    [InlineData("/ws/cs/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("/api/CP-URL")]
    [InlineData("/health/CP-URL")]
    public async Task MissingInvalidOrReservedUrlIdentityDoesNotDiscoverADevice(string path)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SocketPathAsync(path));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .OcppChargePoints.ToListAsync(timeout.Token)
        );
    }

    [Theory]
    [InlineData("/CP-URL")]
    [InlineData("/ws/CP-URL")]
    [InlineData("/ocpp/CP-URL")]
    [InlineData("/ws/cs/CP-URL/CP-URL")]
    public async Task CompatibilityRoutesStillRequireOcpp16BeforeDeviceDiscovery(string path)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("ocpp2.0.1");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(new Uri("ws://localhost" + path), timeout.Token)
        );
        Assert.Contains("400", error.Message);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .OcppChargePoints.ToListAsync(timeout.Token)
        );
    }

    [Fact]
    public async Task AlternateUrlsCannotBypassDisabledDeviceSimulatorPolicyOrActiveConnection()
    {
        using (var socket = await SocketPathAsync("/CP-URL"))
        {
            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SocketPathAsync("/ws/cs/CP-URL/CP-URL")
            );
            Assert.Contains("409", duplicate.Message);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.OcppChargePoints.SingleAsync(timeout.Token)).IsEnabled = false;
            await db.SaveChangesAsync(timeout.Token);
        }
        foreach (
            var path in new[]
            {
                "/CP-URL",
                "/ocpp/CP-URL",
                "/ws/cs/CP-URL/CP-URL",
                "/SIM-001",
                "/ocpp/SIM-001",
            }
        )
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SocketPathAsync(path)
            );
            Assert.Contains("401", error.Message);
        }
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Single(await finalDb.OcppChargePoints.ToListAsync(timeout.Token));
        Assert.Single(await finalDb.OcppConnections.ToListAsync(timeout.Token));
    }

    [Fact]
    public async Task CompatibilityDoesNotDiscoverDevicesFromOrdinaryHttpOrReplaceApiAndHealthEndpoints()
    {
        using var client = factory.CreateClient();
        foreach (var path in CompatiblePaths.Skip(1))
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(path, timeout.Token)).StatusCode
            );
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/health/live", timeout.Token)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/admin/logs", timeout.Token)).StatusCode
        );
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .OcppChargePoints.ToListAsync(timeout.Token)
        );
    }
}

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVCharging.Api.Ocpp;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;

namespace EVCharging.Api.Endpoints;

public static class OcppEndpoints
{
    private const int MaximumFrameBytes = 65_536;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void MapOcppEndpoints(this WebApplication app)
    {
        app.MapGet("/ws/cs/{identity}", ConnectAsync)
            .WithMetadata(new OcppEndpointRoute(false))
            .AllowAnonymous()
            .RequireRateLimiting("device-connect");
        app.MapGet("/{**ocppPath}", ConnectAsync)
            .WithOrder(int.MaxValue)
            .WithMetadata(new OcppEndpointRoute(true))
            .AllowAnonymous()
            .RequireRateLimiting("device-connect");
    }

    private static async Task ConnectAsync(
        HttpContext context,
        IOcppGateway gateway,
        OcppConnectionRegistry registry,
        IServiceScopeFactory scopes,
        ILoggerFactory logs,
        IApplicationDiagnostics diagnostics,
        IHostApplicationLifetime lifetime
    )
    {
        var logger = logs.CreateLogger("OcppWebSocket");
        var compatibility =
            context.GetEndpoint()?.Metadata.GetMetadata<OcppEndpointRoute>()?.Compatibility == true;
        var identity = OcppPath.ExtractIdentity(context.Request.Path.Value);
        if (compatibility && (!context.WebSockets.IsWebSocketRequest || identity is null))
        {
            context.Response.StatusCode = 404;
            return;
        }
        if (identity is null)
        {
            logger.LogWarning("OCPP connection rejected: HTTP 401, invalid charge point identity.");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { code = "device_connection_denied" });
            return;
        }
        var offeredOcpp16 = context.WebSockets.WebSocketRequestedProtocols.Contains("ocpp1.6");
        logger.LogInformation(
            "OCPP connection attempt {Identity}: HTTP {Protocol}, WebSocket={WebSocket}, Ocpp16={Ocpp16}.",
            identity,
            context.Request.Protocol,
            context.WebSockets.IsWebSocketRequest,
            offeredOcpp16
        );
        if (!context.WebSockets.IsWebSocketRequest || !offeredOcpp16)
        {
            logger.LogWarning(
                "OCPP connection {Identity} rejected: HTTP 400, ocpp1.6 WebSocket required.",
                identity
            );
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { code = "ocpp16_websocket_required" });
            return;
        }
        var credentials = BasicCredentials(context.Request.Headers.Authorization.ToString());
        var deviceId = await gateway.AuthenticateAsync(
            identity,
            credentials?.Username ?? "",
            credentials?.Password ?? "",
            context.RequestAborted
        );
        if (deviceId is null)
        {
            logger.LogWarning(
                "OCPP connection {Identity} rejected: HTTP 401, device admission denied.",
                identity
            );
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { code = "device_connection_denied" });
            return;
        }
        if (!registry.TryOpen(deviceId.Value))
        {
            logger.LogWarning(
                "OCPP connection {Identity} rejected: HTTP 409, an existing socket is still open.",
                identity
            );
            context.Response.StatusCode = 409;
            return;
        }

        Guid? connectionId = null;
        WebSocket? socket = null;
        var started = Stopwatch.GetTimestamp();
        var receivedMessages = 0;
        var sentReplies = 0;
        var closeReason = "connection_ended";
        int? serverCloseCode = null;
        IDisposable? connectionScope = null;
        try
        {
            connectionId = await gateway.OpenAsync(deviceId.Value, context.RequestAborted);
            connectionScope = diagnostics.BeginScope(new(ConnectionId: connectionId));
            socket = await context.WebSockets.AcceptWebSocketAsync("ocpp1.6");
            logger.LogInformation(
                new EventId(1001, "websocket_connected"),
                "OCPP device {Identity} connected.",
                identity
            );
            var buffer = new byte[4096];
            while (
                socket.State == WebSocketState.Open
                && !context.RequestAborted.IsCancellationRequested
            )
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(
                    context.RequestAborted,
                    lifetime.ApplicationStopping
                );
                idle.CancelAfter(TimeSpan.FromMinutes(3));
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer, idle.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        closeReason = "peer_close";
                        logger.LogInformation(
                            "OCPP device {Identity} sent Close: code={CloseCode}, received={Received}, replied={Replied}.",
                            identity,
                            (int?)received.CloseStatus,
                            receivedMessages,
                            sentReplies
                        );
                        // Commit the disconnect before acknowledging it so a
                        // subsequent snapshot cannot still show a live device.
                        using var closeTimeout = new CancellationTokenSource(
                            TimeSpan.FromSeconds(10)
                        );
                        await using var closeScope = scopes.CreateAsyncScope();
                        await closeScope
                            .ServiceProvider.GetRequiredService<IOcppGateway>()
                            .CloseAsync(
                                deviceId.Value,
                                connectionId.Value,
                                (int?)received.CloseStatus,
                                closeTimeout.Token,
                                closeReason
                            );
                        connectionId = null;
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Disconnected",
                            closeTimeout.Token
                        );
                        return;
                    }
                    if (
                        received.MessageType != WebSocketMessageType.Text
                        || message.Length + received.Count > MaximumFrameBytes
                    )
                    {
                        closeReason = "invalid_frame_type_or_size";
                        serverCloseCode = (int)WebSocketCloseStatus.MessageTooBig;
                        logger.LogWarning(
                            "OCPP device {Identity} rejected frame: type={FrameType}, bytes={FrameBytes}.",
                            identity,
                            received.MessageType,
                            message.Length + received.Count
                        );
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "Text frames up to 64 KiB are supported.",
                            idle.Token
                        );
                        return;
                    }
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);

                receivedMessages++;
                if (receivedMessages == 1)
                    logger.LogInformation(
                        "OCPP device {Identity} sent its first text message: {FrameBytes} bytes after {ElapsedMs} ms.",
                        identity,
                        message.Length,
                        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
                    );

                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOcppGateway>();
                if (
                    !await processor.IsConnectionCurrentAsync(
                        deviceId.Value,
                        connectionId.Value,
                        idle.Token
                    )
                )
                {
                    closeReason = "connection_permission_revoked";
                    serverCloseCode = (int)WebSocketCloseStatus.PolicyViolation;
                    logger.LogWarning(
                        "OCPP device {Identity} connection permission was revoked.",
                        identity
                    );
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        "Device connection permission revoked.",
                        idle.Token
                    );
                    return;
                }
                var frame = Utf8.GetString(message.ToArray());
                var reply = await processor.ProcessAsync(
                    deviceId.Value,
                    connectionId.Value,
                    frame,
                    idle.Token
                );
                await socket.SendAsync(
                    Utf8.GetBytes(reply),
                    WebSocketMessageType.Text,
                    true,
                    idle.Token
                );
                sentReplies++;
                // Processing commits before SendAsync. This separate event records the successful send.
                using var replyDocument = JsonDocument.Parse(reply);
                using var callDocument = JsonDocument.Parse(frame);
                var action = callDocument.RootElement[2].GetString();
                var replyType = replyDocument.RootElement[0].GetInt32();
                logger.Log(
                    action == "MeterValues" && replyType == 3
                        ? LogLevel.Debug
                        : LogLevel.Information,
                    new EventId(1002, "reply_sent"),
                    "OCPP reply sent for {Identity}: action={Action}, call={CallId}, type={ReplyType}, bytes={FrameBytes}.",
                    identity,
                    action,
                    replyDocument.RootElement[1].GetString(),
                    replyType,
                    Utf8.GetByteCount(reply)
                );
            }
        }
        catch (Exception error)
            when (error
                    is OperationCanceledException
                        or WebSocketException
                        or JsonException
                        or DecoderFallbackException
            )
        {
            closeReason = error switch
            {
                OperationCanceledException
                    when lifetime.ApplicationStopping.IsCancellationRequested => "server_shutdown",
                OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                    "request_aborted",
                OperationCanceledException => "idle_or_processing_timeout",
                WebSocketException => "transport_error",
                _ => "invalid_payload",
            };
            var requestedCloseCode =
                closeReason == "server_shutdown"
                    ? (int)WebSocketCloseStatus.EndpointUnavailable
                    : (int)WebSocketCloseStatus.InvalidPayloadData;
            serverCloseCode = null;
            logger.LogWarning(
                new EventId(1003, "transport_disconnected"),
                "OCPP device {Identity} disconnected: {ErrorType}; websocketError={WebSocketError}; requestAborted={RequestAborted}; state={SocketState}; received={Received}; replied={Replied}; elapsedMs={ElapsedMs}; reason={FailureCode}.",
                identity,
                error.GetType().Name,
                (error as WebSocketException)?.WebSocketErrorCode,
                context.RequestAborted.IsCancellationRequested,
                socket?.State,
                receivedMessages,
                sentReplies,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                closeReason
            );
            if (socket?.State == WebSocketState.Open)
                serverCloseCode = await TryCloseOutputAsync(
                    socket,
                    (WebSocketCloseStatus)requestedCloseCode,
                    closeReason
                )
                    ? requestedCloseCode
                    : null;
        }
        catch (Exception error)
        {
            closeReason = "processing_failed";
            serverCloseCode = null;
            logger.LogError(
                error,
                "OCPP processing failed for {Identity}; no success acknowledgement was sent.",
                identity
            );
            // Before HTTP upgrade, preserve the normal API error status (including DB 503).
            if (socket is null)
                throw;
            if (socket?.State == WebSocketState.Open)
                serverCloseCode = await TryCloseOutputAsync(
                    socket,
                    WebSocketCloseStatus.InternalServerError,
                    closeReason
                )
                    ? (int)WebSocketCloseStatus.InternalServerError
                    : null;
        }
        finally
        {
            logger.LogInformation(
                "OCPP connection {Identity} ended: received={Received}, replied={Replied}, closeCode={CloseCode}, elapsedMs={ElapsedMs}, reason={FailureCode}.",
                identity,
                receivedMessages,
                sentReplies,
                (int?)socket?.CloseStatus ?? serverCloseCode,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                closeReason
            );
            try
            {
                if (connectionId is { } id)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await using var scope = scopes.CreateAsyncScope();
                    await scope
                        .ServiceProvider.GetRequiredService<IOcppGateway>()
                        .CloseAsync(
                            deviceId.Value,
                            id,
                            (int?)socket?.CloseStatus ?? serverCloseCode,
                            timeout.Token,
                            closeReason
                        );
                }
            }
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "OCPP disconnect persistence failed for {Identity}.",
                    identity
                );
            }
            socket?.Dispose();
            registry.Close(deviceId.Value);
            connectionScope?.Dispose();
        }
    }

    private static async Task<bool> TryCloseOutputAsync(
        WebSocket socket,
        WebSocketCloseStatus code,
        string reason
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await socket.CloseOutputAsync(code, reason, timeout.Token);
            return true;
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
            socket.Abort();
            return false;
        }
    }

    private static (string Username, string Password)? BasicCredentials(string header)
    {
        if (
            header.Length > 1024
            || !AuthenticationHeaderValue.TryParse(header, out var authorization)
            || !authorization.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase)
            || authorization.Parameter is null
        )
            return null;
        try
        {
            var decoded = Utf8.GetString(Convert.FromBase64String(authorization.Parameter));
            var colon = decoded.IndexOf(':');
            return colon > 0 ? (decoded[..colon], decoded[(colon + 1)..]) : null;
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        {
            return null;
        }
    }
}

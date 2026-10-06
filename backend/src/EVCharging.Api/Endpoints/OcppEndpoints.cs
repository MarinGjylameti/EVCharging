using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVCharging.Api.Ocpp;
using EVCharging.Application.Charging;

namespace EVCharging.Api.Endpoints;

public static class OcppEndpoints
{
    private const int MaximumFrameBytes = 65_536;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void MapOcppEndpoints(this WebApplication app) =>
        app.MapGet("/ws/cs/{identity}", ConnectAsync)
            .AllowAnonymous()
            .RequireRateLimiting("device-connect");

    private static async Task ConnectAsync(
        HttpContext context,
        string identity,
        IOcppGateway gateway,
        OcppConnectionRegistry registry,
        IServiceScopeFactory scopes,
        ILoggerFactory logs
    )
    {
        var logger = logs.CreateLogger("OcppWebSocket");
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
        try
        {
            connectionId = await gateway.OpenAsync(deviceId.Value, context.RequestAborted);
            socket = await context.WebSockets.AcceptWebSocketAsync("ocpp1.6");
            logger.LogInformation("OCPP device {Identity} connected.", identity);
            var buffer = new byte[4096];
            while (
                socket.State == WebSocketState.Open
                && !context.RequestAborted.IsCancellationRequested
            )
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(
                    context.RequestAborted
                );
                idle.CancelAfter(TimeSpan.FromMinutes(3));
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer, idle.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
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
                                closeTimeout.Token
                            );
                        connectionId = null;
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Disconnected",
                            CancellationToken.None
                        );
                        return;
                    }
                    if (
                        received.MessageType != WebSocketMessageType.Text
                        || message.Length + received.Count > MaximumFrameBytes
                    )
                    {
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
                var reply = await processor.ProcessAsync(
                    deviceId.Value,
                    connectionId.Value,
                    Utf8.GetString(message.ToArray()),
                    idle.Token
                );
                await socket.SendAsync(
                    Utf8.GetBytes(reply),
                    WebSocketMessageType.Text,
                    true,
                    idle.Token
                );
                sentReplies++;
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
            logger.LogInformation(
                "OCPP device {Identity} disconnected: {ErrorType}; websocketError={WebSocketError}; requestAborted={RequestAborted}; state={SocketState}; received={Received}; replied={Replied}; elapsedMs={ElapsedMs}.",
                identity,
                error.GetType().Name,
                (error as WebSocketException)?.WebSocketErrorCode,
                context.RequestAborted.IsCancellationRequested,
                socket?.State,
                receivedMessages,
                sentReplies,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            if (socket?.State == WebSocketState.Open)
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Invalid frame or connection timeout.",
                    CancellationToken.None
                );
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "OCPP processing failed for {Identity}; no success acknowledgement was sent.",
                identity
            );
            if (socket?.State == WebSocketState.Open)
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "Message could not be committed.",
                    CancellationToken.None
                );
        }
        finally
        {
            logger.LogInformation(
                "OCPP connection {Identity} ended: received={Received}, replied={Replied}, closeCode={CloseCode}, elapsedMs={ElapsedMs}.",
                identity,
                receivedMessages,
                sentReplies,
                (int?)socket?.CloseStatus,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            try
            {
                if (connectionId is { } id)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await using var scope = scopes.CreateAsyncScope();
                    await scope
                        .ServiceProvider.GetRequiredService<IOcppGateway>()
                        .CloseAsync(deviceId.Value, id, (int?)socket?.CloseStatus, timeout.Token);
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

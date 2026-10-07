using System.Diagnostics;
using EVCharging.Api.Ocpp;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;

namespace EVCharging.Api.Hosting;

public sealed class ApplicationRequestDiagnostics(
    RequestDelegate next,
    IApplicationDiagnostics diagnostics
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var ocpp = path.StartsWithSegments("/ws") || context.WebSockets.IsWebSocketRequest;
        var health = path.StartsWithSegments("/health");
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
        using var scope = diagnostics.BeginScope(new(TraceId: context.TraceIdentifier));
        // Avoid filling the buffer with its own polling requests. Query strings, bodies,
        // headers, credentials and personal account data are never recorded.
        if (
            (!ocpp && !health && !path.StartsWithSegments("/api/v1"))
            || path.StartsWithSegments("/api/v1/admin/logs")
        )
        {
            await next(context);
            return;
        }
        var started = Stopwatch.GetTimestamp();
        var identity = ocpp ? OcppPath.ExtractIdentity(path.Value) : null;
        var endpoint = context.GetEndpoint();
        var compatibility =
            endpoint?.Metadata.GetMetadata<OcppEndpointRoute>()?.Compatibility == true;
        var routeMatched =
            endpoint is not null
            && (!compatibility || context.WebSockets.IsWebSocketRequest && identity is not null);
        // Only a bounded path, never a query string, Authorization, cookies, or body.
        var safePath = ocpp
            ? new string(
                (path.Value ?? "")
                    .Take(200)
                    .Select(c =>
                        char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' ? c : '_'
                    )
                    .ToArray()
            )
            : (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "[unmatched route]";
        if (ocpp)
            diagnostics.Record(
                "OCPP",
                "Information",
                $"Handshake received: {context.Request.Method} {safePath}; WebSocket={context.WebSockets.IsWebSocketRequest}; Ocpp16={context.WebSockets.WebSocketRequestedProtocols.Contains("ocpp1.6")}; routeMatched={routeMatched}.",
                identity,
                new(EventName: "handshake_received")
            );
        int? failureStatus = null;
        try
        {
            await next(context);
        }
        catch
        {
            failureStatus = context.RequestAborted.IsCancellationRequested ? 499 : 500;
            throw;
        }
        finally
        {
            var status = failureStatus ?? context.Response.StatusCode;
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!health || status >= 400)
                diagnostics.Record(
                    ocpp ? "OCPP" : "HTTP",
                    status >= 500 ? "Error"
                        : status >= 400 ? "Warning"
                        : "Information",
                    $"{context.Request.Method} {safePath} → HTTP {status} ({elapsed} ms).",
                    identity,
                    new(EventName: "request_completed", DurationMs: elapsed, HttpStatus: status)
                );
        }
    }
}

using System.Diagnostics;
using EVCharging.Application.Charging;

namespace EVCharging.Api.Hosting;

public sealed class ApplicationRequestDiagnostics(
    RequestDelegate next,
    IApplicationDiagnostics diagnostics
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var ocpp = path.StartsWithSegments("/ws");
        // Avoid filling the buffer with its own polling requests. Query strings, bodies,
        // headers, credentials and personal account data are never recorded.
        if ((!ocpp && !path.StartsWithSegments("/api/v1")) || path == "/api/v1/admin/logs")
        {
            await next(context);
            return;
        }
        var started = Stopwatch.GetTimestamp();
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
            var identity = path.StartsWithSegments("/ws/cs", out var remaining)
                ? remaining.Value?.Trim('/')
                : null;
            diagnostics.Record(
                ocpp ? "OCPP" : "HTTP",
                status >= 500 ? "Error"
                    : status >= 400 ? "Warning"
                    : "Information",
                $"{context.Request.Method} {path} → HTTP {status} ({elapsed} ms).",
                identity
            );
        }
    }
}

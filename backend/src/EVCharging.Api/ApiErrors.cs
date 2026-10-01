using EVCharging.Application.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVCharging.Api;

public sealed class ApiErrors(RequestDelegate next, ILogger<ApiErrors> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, code, message) = ex switch
            {
                RequestFailure failure => (failure.Status, failure.Code, failure.Message),
                DbUpdateConcurrencyException => (412, "version_conflict", "The record changed. Refresh and try again."),
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                    (409, "duplicate_resource", "The resource already exists."),
                BadHttpRequestException => (400, "invalid_request", "The request is not valid."),
                OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                    (499, "request_cancelled", "The request was cancelled."),
                NpgsqlException => (503, "service_unavailable", "The service is temporarily unavailable."),
                _ => (500, "server_error", "An unexpected error occurred.")
            };
            if (status >= 500) logger.LogError(ex, "API failure {Code}, trace {TraceId}", code, context.TraceIdentifier);
            context.Response.StatusCode = status;
            if (!context.RequestAborted.IsCancellationRequested)
                await context.Response.WriteAsJsonAsync(new { status, code, title = message, traceId = context.TraceIdentifier });
        }
    }
}

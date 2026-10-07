using EVCharging.Application.Charging;
using EVCharging.Application.Common;

namespace EVCharging.Api.Endpoints;

public static class ApplicationLogEndpoints
{
    public static void MapApplicationLogEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/v1/admin/logs/history",
                async (
                    string? source,
                    string? identity,
                    string? level,
                    string? traceId,
                    Guid? connectionId,
                    DateTimeOffset? from,
                    DateTimeOffset? until,
                    long? beforeId,
                    int? limit,
                    IApplicationLogHistory history,
                    CancellationToken ct
                ) =>
                {
                    if (
                        source is not (null or "OCPP" or "HTTP" or "Worker" or "System")
                        || level
                            is not (null or "Information" or "Warning" or "Error" or "Critical")
                        || identity?.Length > 40
                        || identity?.Any(char.IsControl) == true
                        || traceId?.Length > 100
                        || traceId?.Any(char.IsControl) == true
                        || from > until
                        || beforeId <= 0
                        || limit is < 1 or > 1000
                    )
                        throw new RequestFailure(
                            400,
                            "invalid_log_filter",
                            "Invalid diagnostic filter, date interval or page size."
                        );
                    return Results.Ok(
                        await history.ReadAsync(
                            new(
                                source,
                                string.IsNullOrWhiteSpace(identity) ? null : identity,
                                level,
                                string.IsNullOrWhiteSpace(traceId) ? null : traceId,
                                connectionId,
                                from,
                                until,
                                beforeId,
                                limit ?? 200
                            ),
                            ct
                        )
                    );
                }
            )
            .RequireAuthorization("Superadmin");
    }
}

using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
        endpoints.MapGet("/health/ready", CheckReadinessAsync);

        return endpoints;
    }

    private static async Task<IResult> CheckReadinessAsync(
        ChargingDbContext db,
        CancellationToken ct
    )
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(503);
        }
        catch
        {
            return Results.StatusCode(503);
        }
    }
}

using EVCharging.Application.Charging;
using Microsoft.Extensions.Options;

namespace EVCharging.Api.Endpoints;

public static class FeatureEndpoints
{
    public static void MapFeatureEndpoints(this WebApplication app) =>
        app.MapGet(
                "/api/v1/features",
                (HttpContext context, IOptions<RuntimeFeatures> features) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(new { features.Value.TestMode });
                }
            )
            .RequireAuthorization();
}

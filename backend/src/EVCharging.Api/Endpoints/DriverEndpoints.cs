using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Application.Charging;

namespace EVCharging.Api.Endpoints;

public static class DriverEndpoints
{
    public static void MapDriverEndpoints(this WebApplication app)
    {
        var driver = app.MapGroup("/api/v1/driver").RequireAuthorization("Driver");
        driver.MapGet(
            "/stations/{stationId:guid}/charging",
            async (
                Guid stationId,
                ClaimsPrincipal user,
                IDriverCharging charging,
                CancellationToken ct,
                int? connector
            ) => Results.Ok(await charging.GetAsync(user.UserId(), stationId, ct, connector))
        );
        driver.MapPost(
            "/stations/{stationId:guid}/connect",
            async (
                Guid stationId,
                ClaimsPrincipal user,
                IDriverCharging charging,
                CancellationToken ct
            ) => Results.Ok(await charging.ConnectAsync(user.UserId(), stationId, ct))
        );
        driver.MapPost(
            "/stations/{stationId:guid}/cancel",
            async (
                Guid stationId,
                ClaimsPrincipal user,
                IDriverCharging charging,
                CancellationToken ct
            ) => Results.Ok(await charging.CancelAsync(user.UserId(), stationId, ct))
        );
        driver.MapGet(
            "/charging-sessions",
            async (ClaimsPrincipal user, IDriverQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.GetChargingStateAsync(user.UserId(), ct))
        );

        driver.MapGet(
            "/stations",
            async (IDriverQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.GetStationsAsync(ct))
        );

        driver.MapGet(
            "/history",
            async (ClaimsPrincipal user, IDriverQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.GetHistoryAsync(user.UserId(), ct))
        );
    }
}

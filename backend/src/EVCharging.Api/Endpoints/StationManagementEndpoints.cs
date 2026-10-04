using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Application.Charging;

namespace EVCharging.Api.Endpoints;

public static class StationManagementEndpoints
{
    public static void MapStationManagementEndpoints(this WebApplication app)
    {
        var stations = app.MapGroup("/api/v1/admin/stations").RequireAuthorization("Operator");

        stations.MapGet(
            "",
            async (ClaimsPrincipal user, IStationManagement service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(user.UserId(), ct))
        );

        stations.MapPost(
            "",
            async (
                StationInput input,
                ClaimsPrincipal user,
                IStationManagement service,
                CancellationToken ct
            ) =>
            {
                var station = await service.CreateAsync(input, user.UserId(), ct);
                return Results.Created($"/api/v1/admin/stations/{station.Id}", station);
            }
        );

        stations.MapPatch(
            "/{id:guid}",
            async (
                Guid id,
                StationInput input,
                ClaimsPrincipal user,
                IStationManagement service,
                CancellationToken ct
            ) => Results.Ok(await service.UpdateAsync(id, input, user.UserId(), ct))
        );

        stations.MapPost(
            "/{id:guid}/archive",
            async (
                Guid id,
                ArchiveStationInput input,
                ClaimsPrincipal user,
                IStationManagement service,
                CancellationToken ct
            ) => Results.Ok(await service.ArchiveAsync(id, input, user.UserId(), ct))
        );
    }
}

using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Application.Charging;

namespace EVCharging.Api.Endpoints;

public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/api/v1/admin").RequireAuthorization("Operator");
        ops.MapGet(
            "/locations",
            async (ClaimsPrincipal u, IOperationsManagement s, CancellationToken ct) =>
                Results.Ok(await s.LocationsAsync(u.UserId(), ct))
        );
        ops.MapPost(
                "/locations",
                async (
                    LocationInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) => Results.Ok(await s.SaveLocationAsync(u.UserId(), null, input, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapPatch(
                "/locations/{id:guid}",
                async (
                    Guid id,
                    LocationInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) => Results.Ok(await s.SaveLocationAsync(u.UserId(), id, input, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapPost(
                "/locations/{id:guid}/archive",
                async (
                    Guid id,
                    ArchiveStationInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) => Results.Ok(await s.ArchiveLocationAsync(u.UserId(), id, input.Version, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapGet(
                "/administrators",
                async (ClaimsPrincipal u, IOperationsManagement s, CancellationToken ct) =>
                    Results.Ok(await s.AdministratorsAsync(u.UserId(), ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapPost(
                "/administrators",
                async (
                    AdministratorInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) => Results.Ok(await s.SaveAdministratorAsync(u.UserId(), null, input, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapPatch(
                "/administrators/{id:guid}",
                async (
                    Guid id,
                    AdministratorInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) => Results.Ok(await s.SaveAdministratorAsync(u.UserId(), id, input, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapPost(
                "/administrators/{id:guid}/archive",
                async (
                    Guid id,
                    ArchiveStationInput input,
                    ClaimsPrincipal u,
                    IOperationsManagement s,
                    CancellationToken ct
                ) =>
                    Results.Ok(await s.ArchiveAdministratorAsync(u.UserId(), id, input.Version, ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapGet(
                "/drivers",
                async (ClaimsPrincipal u, IOperationsManagement s, CancellationToken ct) =>
                    Results.Ok(await s.DriversAsync(u.UserId(), ct))
            )
            .RequireAuthorization("Superadmin");
        ops.MapGet(
            "/summary",
            async (ClaimsPrincipal u, IOperationsManagement s, CancellationToken ct) =>
                Results.Ok(await s.SummaryAsync(u.UserId(), ct))
        );
        ops.MapGet(
            "/sessions",
            async (
                Guid? stationId,
                Guid? locationId,
                string? status,
                DateTimeOffset? from,
                DateTimeOffset? until,
                string? driver,
                int? page,
                ClaimsPrincipal u,
                IOperationsManagement s,
                CancellationToken ct
            ) =>
                Results.Ok(
                    await s.SessionsAsync(
                        u.UserId(),
                        new(stationId, locationId, status, from, until, driver, page ?? 1),
                        ct
                    )
                )
        );
        ops.MapGet(
            "/audit",
            async (
                Guid? stationId,
                ClaimsPrincipal u,
                IOperationsManagement s,
                CancellationToken ct
            ) => Results.Ok(await s.AuditAsync(u.UserId(), stationId, ct))
        );
        ops.MapGet(
            "/prices",
            async (
                Guid? stationId,
                ClaimsPrincipal u,
                IOperationsManagement s,
                CancellationToken ct
            ) => Results.Ok(await s.PricesAsync(u.UserId(), stationId, ct))
        );
        ops.MapGet(
            "/stations/{id:guid}/device",
            async (Guid id, ClaimsPrincipal u, IOperationsManagement s, CancellationToken ct) =>
                Results.Ok(await s.DeviceAsync(u.UserId(), id, ct))
        );
        ops.MapPost(
            "/stations/{id:guid}/device",
            async (
                Guid id,
                DeviceInput input,
                ClaimsPrincipal u,
                IOperationsManagement s,
                HttpContext context,
                CancellationToken ct
            ) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await s.ProvisionDeviceAsync(u.UserId(), id, input, ct));
            }
        );
        ops.MapPatch(
            "/stations/{id:guid}/device",
            async (
                Guid id,
                DeviceControlInput input,
                ClaimsPrincipal u,
                IOperationsManagement s,
                HttpContext context,
                CancellationToken ct
            ) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await s.ControlDeviceAsync(u.UserId(), id, input, ct));
            }
        );
        ops.MapGet("/events", OperationsEventsEndpoints.StreamAsync);
    }
}

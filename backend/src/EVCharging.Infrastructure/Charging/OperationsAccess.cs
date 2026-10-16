using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class OperationsAccess(
    ChargingDbContext db,
    TimeProvider clock,
    IOptions<RuntimeFeatures> features
)
{
    public bool TestMode => features.Value.TestMode;

    public async Task<Account> ActorAsync(Guid id, CancellationToken ct, bool superadmin = false)
    {
        var actor = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == id && x.IsActive && x.ArchivedAt == null,
            ct
        );
        if (
            actor is null
            || actor.Role == UserRole.Driver
            || superadmin && actor.Role != UserRole.Superadmin
        )
            throw new RequestFailure(
                403,
                "access_denied",
                "This account cannot manage this resource."
            );
        return actor;
    }

    public IQueryable<ChargingStation> Stations(Account actor) =>
        (
            actor.Role == UserRole.Superadmin
                ? db.Stations
                : db.Stations.Where(s =>
                    db.AdministratorGrants.Any(g =>
                        g.AdministratorId == actor.Id
                        && (
                            g.StationId == s.Id
                            || s.LocationId != null && g.LocationId == s.LocationId
                        )
                    )
                )
        ).Where(s => features.Value.TestMode || !s.IsDemo);

    public IQueryable<ChargingLocation> Locations(Account actor) =>
        actor.Role == UserRole.Superadmin
            ? db.Locations
            : db.Locations.Where(l =>
                db.AdministratorGrants.Any(g =>
                    g.AdministratorId == actor.Id && g.LocationId == l.Id
                )
            );

    public async Task<ChargingStation> StationAsync(Account actor, Guid id, CancellationToken ct) =>
        await Stations(actor).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new RequestFailure(404, "station_not_found", "Station not found.");

    public async Task DestinationAsync(Account actor, Guid? locationId, CancellationToken ct)
    {
        if (locationId is null)
        {
            if (actor.Role != UserRole.Superadmin)
                throw new RequestFailure(
                    403,
                    "location_assignment_required",
                    "Select an assigned location."
                );
            return;
        }
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM charging_locations WHERE id = {locationId.Value} FOR UPDATE",
                ct
            );
        if (!await Locations(actor).AnyAsync(x => x.Id == locationId && !x.IsArchived, ct))
            throw new RequestFailure(
                403,
                "location_assignment_required",
                "Select an active assigned location."
            );
    }

    public void Audit(
        Account actor,
        Guid entity,
        string action,
        object? before,
        object? after,
        string? reason = null
    ) =>
        db.Audit.Add(
            new AuditEntry
            {
                ActorId = actor.Id,
                ActorRole = actor.Role.ToString(),
                EntityId = entity,
                Action = action,
                RecordedAt = clock.GetUtcNow(),
                ChangesJson = JsonSerializer.Serialize(
                    new
                    {
                        before,
                        after,
                        reason,
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                ),
            }
        );
}

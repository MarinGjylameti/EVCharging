using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService
{
    public async Task<OperationsSummary> SummaryAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await access.ActorAsync(actorId, ct);
        var stations = access.Stations(actor).Where(x => !x.IsArchived);
        var status = await stations
            .GroupBy(x => x.Status)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var ids = access.Stations(actor).Select(x => x.Id);
        var sessions = db.ChargingSessions.AsNoTracking().Where(x => ids.Contains(x.PointId));
        var active = await sessions.CountAsync(x => x.EndedAt == null, ct);
        var completed = await sessions.CountAsync(x => x.EndedAt != null, ct);
        var energy = await sessions.SumAsync(x => x.EnergyKWh ?? 0, ct);
        var cost = await sessions.SumAsync(
            x => decimal.Floor((x.EnergyKWh ?? 0) * x.UnitPriceSnapshot * 100 + 0.5m) / 100,
            ct
        );
        return new(
            status.Sum(x => x.Count),
            Enum.GetValues<PointState>()
                .ToDictionary(
                    x => x.ToString(),
                    x => status.Where(g => g.State == x).Sum(g => g.Count)
                ),
            await access.Locations(actor).CountAsync(x => !x.IsArchived, ct),
            actor.Role == UserRole.Superadmin
                ? await db.Accounts.CountAsync(
                    x => x.Role == UserRole.Admin && x.IsActive && x.ArchivedAt == null,
                    ct
                )
                : 0,
            actor.Role == UserRole.Superadmin
                ? await db.Accounts.CountAsync(
                    x => x.Role == UserRole.Driver && x.IsActive && x.ArchivedAt == null,
                    ct
                )
                : 0,
            active,
            completed,
            energy,
            cost,
            "EUR",
            clock.GetUtcNow()
        );
    }

    public async Task<SessionPage> SessionsAsync(
        Guid actorId,
        SessionFilter filter,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        if (
            filter.Page is < 1 or > 100000
            || filter.Driver?.Length > 254
            || filter.From > filter.Until
        )
            throw new RequestFailure(400, "invalid_session_filter", "Invalid session filter.");
        var stations = access.Stations(actor);
        if (filter.StationId is { } sid)
            stations = stations.Where(x => x.Id == sid);
        if (filter.LocationId is { } lid)
            stations = stations.Where(x => x.LocationId == lid);
        var ids = stations.Select(x => x.Id);
        var query = db.ChargingSessions.AsNoTracking().Where(x => ids.Contains(x.PointId));
        if (filter.Status == "Active")
            query = query.Where(x => x.EndedAt == null);
        else if (filter.Status == "Completed")
            query = query.Where(x => x.EndedAt != null);
        else if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (
                !Enum.TryParse<ChargingState>(filter.Status, false, out var status)
                || !Enum.IsDefined(status)
            )
                throw new RequestFailure(400, "invalid_session_filter", "Invalid charging status.");
            query = query.Where(x => x.Status == status);
        }
        if (filter.From is { } from)
            query = query.Where(x => x.StartedAt >= from);
        if (filter.Until is { } until)
            query = query.Where(x => x.StartedAt < until);
        if (!string.IsNullOrWhiteSpace(filter.Driver))
        {
            var text = filter.Driver.Trim().ToUpperInvariant();
            query = query.Where(s =>
                db.ChargingConnectionRequests.Any(c =>
                    c.SessionId == s.Id
                    && db.Accounts.Any(a =>
                        a.Id == c.DriverId
                        && (
                            a.NormalizedEmail.Contains(text)
                            || (a.FirstName + " " + a.LastName).ToUpper().Contains(text)
                        )
                    )
                )
            );
        }
        var total = await query.CountAsync(ct);
        var page = await query
            .OrderByDescending(x => x.StartedAt)
            .ThenBy(x => x.Id)
            .Skip((filter.Page - 1) * 50)
            .Take(50)
            .ToListAsync(ct);
        var stationIds = page.Select(x => x.PointId).Distinct().ToArray();
        var stationRows = await db
            .Stations.AsNoTracking()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var sessionIds = page.Select(x => x.Id).ToArray();
        var owners = await (
            from claim in db.ChargingConnectionRequests.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on claim.DriverId equals account.Id
            where sessionIds.Contains(claim.SessionId ?? Guid.Empty)
            select new
            {
                claim.SessionId,
                account.Id,
                account.FirstName,
                account.LastName,
                account.Email,
            }
        ).ToListAsync(ct);
        var connectors = await db
            .ChargingConnectors.AsNoTracking()
            .Where(x => stationIds.Contains(x.StationId))
            .ToDictionaryAsync(x => x.Id, ct);
        var now = clock.GetUtcNow();
        return new(
            page.Select(s =>
                {
                    var station = stationRows[s.PointId];
                    var owner = owners.SingleOrDefault(x => x.SessionId == s.Id);
                    var online =
                        connectors.TryGetValue(s.ConnectorId, out var connector)
                        && connector.IsOnline;
                    var state =
                        s.EndedAt != null ? "Completed"
                        : !online ? "ConnectionLost"
                        : s.Status.ToString();
                    var power =
                        s.EndedAt != null ? 0
                        : s.Status == ChargingState.Charging
                        && online
                        && s.PowerReceivedAt > now.AddSeconds(-120)
                            ? s.PowerKW
                        : null;
                    return new OperationsSession(
                        s.Id,
                        s.PointId,
                        station.Name,
                        station.Code,
                        station.LocationId,
                        s.ConnectorNumber,
                        owner?.Id,
                        owner is null ? null : owner.FirstName + " " + owner.LastName,
                        owner?.Email,
                        state,
                        s.StartedAt,
                        s.EndedAt,
                        s.EnergyKWh,
                        power,
                        s.StateOfCharge,
                        s.UnitPriceSnapshot,
                        s.CalculatedCost,
                        s.Currency,
                        s.IsDemo
                    );
                })
                .ToArray(),
            total,
            filter.Page,
            50
        );
    }

    public async Task<IReadOnlyList<AuditDto>> AuditAsync(
        Guid actorId,
        Guid? stationId,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        var query = db.Audit.AsNoTracking();
        if (stationId is { } id)
        {
            await access.StationAsync(actor, id, ct);
            query = query.Where(x => x.EntityId == id);
        }
        else if (actor.Role != UserRole.Superadmin)
        {
            var stationIds = access.Stations(actor).Select(x => x.Id);
            var locations = access.Locations(actor).Select(x => x.Id);
            query = query.Where(x =>
                stationIds.Contains(x.EntityId) || locations.Contains(x.EntityId)
            );
        }
        return await query
            .OrderByDescending(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Take(200)
            .Select(x => new AuditDto(
                x.Id,
                x.ActorId,
                x.ActorRole,
                x.Action,
                x.EntityId,
                x.ChangesJson,
                x.RecordedAt,
                db.Accounts.Where(a => a.Id == x.ActorId)
                    .Select(a => a.FirstName + " " + a.LastName)
                    .FirstOrDefault(),
                db.Stations.Where(s => s.Id == x.EntityId).Select(s => s.Name).FirstOrDefault()
                    ?? db.Locations.Where(l => l.Id == x.EntityId)
                        .Select(l => l.Name)
                        .FirstOrDefault()
                    ?? db.Accounts.Where(a => a.Id == x.EntityId)
                        .Select(a => a.FirstName + " " + a.LastName)
                        .FirstOrDefault()
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PriceRevisionDto>> PricesAsync(
        Guid actorId,
        Guid? stationId,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        var ids = access.Stations(actor).Select(x => x.Id);
        if (stationId is { } id)
        {
            await access.StationAsync(actor, id, ct);
            ids = ids.Where(x => x == id);
        }
        return await db
            .PriceRevisions.AsNoTracking()
            .Where(x => ids.Contains(x.StationId))
            .OrderByDescending(x => x.RecordedAt)
            .ThenBy(x => x.Id)
            .Take(200)
            .Select(x => new PriceRevisionDto(
                x.Id,
                x.StationId,
                x.PreviousPrice,
                x.PricePerKWh,
                x.Currency,
                x.ActorId,
                x.Reason,
                x.RecordedAt
            ))
            .ToListAsync(ct);
    }
}

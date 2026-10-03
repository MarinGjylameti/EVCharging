using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverChargingService(
    ChargingDbContext db,
    IDriverQueries queries,
    TimeProvider clock,
    StationEventSignal events,
    IChargingClaimVerification verification,
    IChargingDiagnostics diagnostics
) : IDriverCharging
{
    public async Task<DriverStationCharging> GetAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var station =
            await db
                .Stations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == stationId && !x.IsArchived, ct)
            ?? throw new RequestFailure(404, "station_not_found", "Station not found.");
        var connector = await db
            .ChargingConnectors.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StationId == stationId && x.Number == 1, ct);
        var now = clock.GetUtcNow();
        var claim = await db
            .ChargingConnectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.StationId == stationId
                    && x.ConnectorNumber == 1
                    && (x.State == "Linked" || x.State == "Waiting" && x.ExpiresAt > now),
                ct
            );
        var own = (
            await queries.GetChargingStateAsync(driverId, ct, stationId)
        ).Sessions.FirstOrDefault();
        return new(
            ChargingRealtime.StationDto(station, now),
            verification.DemoBypassEnabled && station.IsDemo && connector is not null,
            connector?.IsOnline == true,
            claim is not null && claim.DriverId != driverId,
            claim?.DriverId == driverId && claim.State == "Waiting"
                ? new(claim.Id, claim.State, claim.ExpiresAt)
                : null,
            own,
            connector is null ? null : ChargingRealtime.ConnectorDto(connector),
            verification.DemoBypassEnabled
            && station.IsDemo
            && connector is not null
            && (claim?.DriverId == driverId || own is not null && claim is null)
                ? await diagnostics.GetAsync(
                    connector.Id,
                    claim?.State == "Waiting" ? null : own?.Id,
                    ct,
                    claim?.State == "Waiting"
                        ? claim.CreatedAt
                        : own?.StartReceivedAt ?? claim?.CreatedAt
                )
                : null
        );
    }

    public async Task<DriverStationCharging> ConnectAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var connector = await verification.VerifyAsync(stationId, ct);
        try
        {
            await using var transaction = await ConnectorMutationLock.BeginAsync(
                db,
                connector.Id,
                driverId,
                ct
            );
            var now = clock.GetUtcNow();
            var expired = await db
                .ChargingConnectionRequests.Where(x =>
                    x.State == "Waiting"
                    && x.ExpiresAt <= now
                    && (
                        x.DriverId == driverId
                        || x.StationId == stationId && x.ConnectorNumber == connector.Number
                    )
                )
                .ToListAsync(ct);
            foreach (var claim in expired)
                claim.State = "Expired";
            if (expired.Count > 0)
                await db.SaveChangesAsync(ct);
            var own = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x => x.DriverId == driverId && (x.State == "Waiting" || x.State == "Linked"),
                ct
            );
            if (
                own is not null
                && (own.StationId != stationId || own.ConnectorNumber != connector.Number)
            )
                throw new RequestFailure(
                    409,
                    "driver_already_connected",
                    "This Driver already has an active charger association."
                );
            var occupied = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x =>
                    x.StationId == stationId
                    && x.ConnectorNumber == connector.Number
                    && (x.State == "Waiting" || x.State == "Linked"),
                ct
            );
            if (occupied is not null && occupied.DriverId != driverId)
                throw new RequestFailure(
                    409,
                    "charger_in_use",
                    "Another Driver owns this connector."
                );
            var active = await db.ChargingSessions.SingleOrDefaultAsync(
                x => x.ConnectorId == connector.Id && x.EndedAt == null,
                ct
            );
            var current =
                own
                ?? new ChargingConnectionRequest
                {
                    DriverId = driverId,
                    StationId = stationId,
                    ConnectorNumber = connector.Number,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(30),
                };
            if (own is null)
                db.ChargingConnectionRequests.Add(current);
            if (active is not null && current.State != "Linked")
            {
                current.State = "Linked";
                current.SessionId = active.Id;
                current.LinkedAt = now;
                // Physical start, energy, tariff and timestamps remain untouched.
            }
            if (own is null || active is not null)
            {
                ChargingRealtime.Stage(
                    db,
                    stationId,
                    "OwnershipChanged",
                    "ChargingClaim",
                    current.Id,
                    0,
                    now,
                    driverId
                );
                ChargingRealtime.Stage(
                    db,
                    stationId,
                    "ConnectorChanged",
                    "ChargingConnector",
                    connector.Id,
                    connector.Version,
                    now
                );
            }
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            events.Notify();
        }
        catch (DbUpdateException error)
            when (error.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                && pg.ConstraintName is "ux_claim_active_driver" or "ux_claim_active_connector"
            )
        {
            var code =
                ((PostgresException)error.InnerException!).ConstraintName
                == "ux_claim_active_driver"
                    ? "driver_already_connected"
                    : "charger_in_use";
            throw new RequestFailure(409, code, "The charging association is already in use.");
        }
        return await GetAsync(driverId, stationId, ct);
    }

    public async Task<DriverStationCharging> CancelAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var connector = await verification.VerifyAsync(stationId, ct);
        await using var transaction = await ConnectorMutationLock.BeginAsync(
            db,
            connector.Id,
            driverId,
            ct
        );
        var waiting = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
            x => x.StationId == stationId && x.DriverId == driverId && x.State == "Waiting",
            ct
        );
        if (waiting is not null)
        {
            waiting.State = "Cancelled";
            ChargingRealtime.Stage(
                db,
                stationId,
                "OwnershipChanged",
                "ChargingClaim",
                waiting.Id,
                0,
                clock.GetUtcNow(),
                driverId
            );
            ChargingRealtime.Stage(
                db,
                stationId,
                "ConnectorChanged",
                "ChargingConnector",
                connector.Id,
                connector.Version,
                clock.GetUtcNow()
            );
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();
        return await GetAsync(driverId, stationId, ct);
    }
}

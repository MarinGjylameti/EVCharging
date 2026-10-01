using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverChargingService(
    ChargingDbContext db,
    IDriverQueries queries,
    TimeProvider clock,
    IOptions<OcppOptions> options
) : IDriverCharging
{
    public async Task<DriverStationCharging> GetAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var station =
            (await queries.GetStationsAsync(ct)).SingleOrDefault(x => x.Id == stationId)
            ?? throw new RequestFailure(404, "station_not_found", "Station not found.");
        var connector = await db
            .OcppConnectors.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StationId == stationId && x.ConnectorNumber == 1, ct);
        var cutoff = clock.GetUtcNow().AddSeconds(-options.Value.OfflineSeconds);
        var connected =
            connector is not null
            && await db.OcppChargePoints.AnyAsync(
                x => x.Id == connector.ChargePointId && x.IsEnabled && x.LastSeenAt >= cutoff,
                ct
            );
        var now = clock.GetUtcNow();
        var waiting = await db
            .ChargingConnectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.StationId == stationId
                    && x.ConnectorNumber == 1
                    && x.State == "Waiting"
                    && x.ExpiresAt > now,
                ct
            );
        var inUse =
            (waiting is not null && waiting.DriverId != driverId)
            || await db.OcppChargingTransactions.AnyAsync(
                x => x.StationId == stationId && x.StoppedAt == null && x.DriverId != driverId,
                ct
            );
        var own = (
            await queries.GetChargingStateAsync(driverId, ct, stationId)
        ).Sessions.FirstOrDefault();
        return new(
            station,
            options.Value.SimulatorEnabled && station.IsDemo && connector is not null,
            connected,
            inUse,
            waiting?.DriverId == driverId
                ? new(waiting.Id, waiting.State, waiting.ExpiresAt)
                : null,
            own
        );
    }

    public async Task<DriverStationCharging> ConnectAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var connector = await RequireDemoConnectorAsync(stationId, ct);
        // Use the same device lock as StartTransaction so two drivers cannot claim
        // one connector, or change the owner of a transaction already in progress.
        await using var transaction = await OcppDeviceUpdate.BeginAsync(
            db,
            connector.ChargePointId,
            ct
        );
        var now = clock.GetUtcNow();
        var active = await db.OcppChargingTransactions.SingleOrDefaultAsync(
            x =>
                x.ChargePointId == connector.ChargePointId
                && x.ConnectorNumber == 1
                && x.StoppedAt == null,
            ct
        );
        if (active is not null)
        {
            if (active.DriverId != driverId)
                throw new RequestFailure(
                    409,
                    "charger_in_use",
                    "This connector is already in use."
                );
            return await GetAsync(driverId, stationId, ct);
        }
        var waiting = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
            x => x.StationId == stationId && x.ConnectorNumber == 1 && x.State == "Waiting",
            ct
        );
        if (waiting is not null && waiting.ExpiresAt <= now)
        {
            waiting.State = "Expired";
            await db.SaveChangesAsync(ct);
            waiting = null;
        }
        if (waiting is not null && waiting.DriverId != driverId)
            throw new RequestFailure(
                409,
                "charger_in_use",
                "Another driver is waiting for this connector."
            );
        if (waiting is null)
            db.ChargingConnectionRequests.Add(
                new()
                {
                    DriverId = driverId,
                    StationId = stationId,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(30),
                }
            );
        StageRequestChange(stationId, driverId, now);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return await GetAsync(driverId, stationId, ct);
    }

    public async Task<DriverStationCharging> CancelAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    )
    {
        var connector = await RequireDemoConnectorAsync(stationId, ct);
        await using var transaction = await OcppDeviceUpdate.BeginAsync(
            db,
            connector.ChargePointId,
            ct
        );
        var waiting = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
            x => x.StationId == stationId && x.DriverId == driverId && x.State == "Waiting",
            ct
        );
        if (waiting is not null)
        {
            waiting.State = "Cancelled";
            StageRequestChange(stationId, driverId, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return await GetAsync(driverId, stationId, ct);
    }

    private async Task<OcppConnector> RequireDemoConnectorAsync(
        Guid stationId,
        CancellationToken ct
    )
    {
        if (
            !options.Value.SimulatorEnabled
            || !await db.Stations.AnyAsync(x => x.Id == stationId && x.IsDemo && !x.IsArchived, ct)
        )
            throw new RequestFailure(
                403,
                "test_connection_unavailable",
                "Test connection is unavailable for this station."
            );
        return await db.OcppConnectors.SingleOrDefaultAsync(
                x => x.StationId == stationId && x.ConnectorNumber == 1,
                ct
            ) ?? throw new RequestFailure(404, "connector_not_found", "Connector not found.");
    }

    private void StageRequestChange(Guid stationId, Guid driverId, DateTimeOffset now)
    {
        db.RealtimeOutbox.Add(
            new()
            {
                EventType = "ConnectionRequested",
                EntityType = "ChargingStation",
                EntityId = stationId,
                Audience = "Driver",
                RecipientAccountId = driverId,
                PayloadJson = JsonSerializer.Serialize(new StationChanged(stationId, 0, "Waiting")),
                CreatedAt = now,
                AvailableAt = now,
            }
        );
    }
}

using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppStationProjection(
    ChargingDbContext db,
    TimeProvider clock,
    IOptions<OcppOptions> options
)
{
    public async Task SetConnectorAsync(
        Guid deviceId,
        int number,
        string status,
        DateTimeOffset observed,
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow();
        if (observed < now.AddMinutes(-5) || observed > now.AddMinutes(5))
            return;
        var connectors = await db
            .OcppConnectors.Where(x =>
                x.ChargePointId == deviceId && (number == 0 || x.ConnectorNumber == number)
            )
            .ToListAsync(ct);
        foreach (var connector in connectors)
        {
            if (number == 0 && status is not ("Unavailable" or "Faulted"))
                continue;
            if (connector.StatusObservedAt is { } previous && observed < previous)
                continue;
            connector.ReportedStatus = status;
            connector.StatusObservedAt = observed;
            connector.StatusReceivedAt = now;
            connector.Version++;
        }
        foreach (
            var stationId in connectors
                .Where(x => x.StationId != null)
                .Select(x => x.StationId!.Value)
                .Distinct()
        )
            await RefreshAsync(stationId, ct);
    }

    public async Task InvalidateDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var connectors = await db
            .OcppConnectors.Where(x => x.ChargePointId == deviceId)
            .ToListAsync(ct);
        foreach (var connector in connectors)
        {
            connector.ReportedStatus = null;
            connector.StatusObservedAt = null;
            connector.StatusReceivedAt = clock.GetUtcNow();
            connector.Version++;
        }
        foreach (
            var stationId in connectors
                .Where(x => x.StationId != null)
                .Select(x => x.StationId!.Value)
                .Distinct()
        )
            await RefreshAsync(stationId, ct);
    }

    public async Task RefreshAsync(Guid stationId, CancellationToken ct)
    {
        var station = await db.Stations.SingleOrDefaultAsync(
            x => x.Id == stationId && !x.IsArchived,
            ct
        );
        if (station is null)
            return;
        var connectors = await db
            .OcppConnectors.Where(x => x.StationId == stationId)
            .ToListAsync(ct);
        var ids = connectors.Select(x => x.ChargePointId).ToArray();
        var cutoff = clock.GetUtcNow().AddSeconds(-options.Value.OfflineSeconds);
        var devices = await db.OcppChargePoints.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var liveIds = devices
            .Where(x => x.IsEnabled && x.LastSeenAt >= cutoff)
            .Select(x => x.Id)
            .ToArray();
        var activeConnectors = await db
            .OcppChargingTransactions.Where(x => x.StationId == stationId && x.StoppedAt == null)
            .Select(x => new { x.ChargePointId, x.ConnectorNumber })
            .ToListAsync(ct);
        var states = connectors
            .Select(x =>
                !liveIds.Contains(x.ChargePointId) ? PointState.Unknown
                : x.ReportedStatus == "Available"
                && activeConnectors.Any(active =>
                    active.ChargePointId == x.ChargePointId
                    && active.ConnectorNumber == x.ConnectorNumber
                )
                    ? PointState.Occupied
                : Map(x.ReportedStatus)
            )
            .ToArray();
        var status = station.ManualRestriction switch
        {
            ManualRestriction.Maintenance => PointState.Maintenance,
            ManualRestriction.OutOfService => PointState.OutOfService,
            _ => Aggregate(states),
        };
        if (station.Status == status && station.StatusSource == "OCPP")
            return;
        station.Status = status;
        station.StatusSource = "OCPP";
        station.StatusObservedAt = connectors
            .Select(x => x.StatusObservedAt)
            .DefaultIfEmpty()
            .Max();
        station.StatusReceivedAt = clock.GetUtcNow();
        station.Version++;
        AvailabilityForecastRules.Clear(station);
        db.RealtimeOutbox.Add(
            new RealtimeOutboxEvent
            {
                EventType = "PointChanged",
                EntityType = "ChargingStation",
                EntityId = station.Id,
                EntityVersion = station.Version,
                PayloadJson = JsonSerializer.Serialize(
                    new StationChanged(station.Id, station.Version, station.Status.ToString())
                ),
                CreatedAt = clock.GetUtcNow(),
                AvailableAt = clock.GetUtcNow(),
            }
        );
    }

    private static PointState Map(string? status) =>
        status switch
        {
            "Available" => PointState.Available,
            "Charging" => PointState.Charging,
            "Preparing" or "Finishing" or "Reserved" or "SuspendedEV" or "SuspendedEVSE" =>
                PointState.Occupied,
            "Unavailable" or "Faulted" => PointState.OutOfService,
            _ => PointState.Unknown,
        };

    private static PointState Aggregate(PointState[] states)
    {
        foreach (
            var state in new[]
            {
                PointState.Available,
                PointState.Charging,
                PointState.Occupied,
                PointState.Unknown,
            }
        )
            if (states.Contains(state))
                return state;
        return states.Length == 0 ? PointState.Unknown : PointState.OutOfService;
    }
}

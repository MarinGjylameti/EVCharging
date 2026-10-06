using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationProjection(ChargingDbContext db, TimeProvider clock)
{
    public async Task RefreshAsync(
        Guid stationId,
        CancellationToken ct,
        AvailabilityForecast? reportedEstimate = null
    )
    {
        var station = await db.Stations.SingleOrDefaultAsync(
            x => x.Id == stationId && !x.IsArchived,
            ct
        );
        if (station is null)
            return;
        var connectors = await db
            .ChargingConnectors.Where(x => x.StationId == stationId)
            .ToListAsync(ct);
        connectors = connectors
            .Concat(db.ChargingConnectors.Local.Where(x => x.StationId == stationId))
            .DistinctBy(x => x.Id)
            .ToList();
        var sessions = await db
            .ChargingSessions.Where(x => x.PointId == stationId && x.EndedAt == null)
            .ToListAsync(ct);
        // Include a start or stop staged in this transaction before SaveChanges.
        sessions = sessions
            .Concat(db.ChargingSessions.Local.Where(x => x.PointId == stationId))
            .DistinctBy(x => x.Id)
            .Where(x => x.EndedAt == null)
            .ToList();
        var active = sessions.Select(x => x.ConnectorId).ToArray();
        var states = connectors
            .Select(x =>
                !x.IsOnline ? PointState.Unknown
                : x.Status == ChargingState.Available && active.Contains(x.Id) ? PointState.Occupied
                : Map(x.Status)
            )
            .ToArray();
        var normal =
            new[]
            {
                PointState.Available,
                PointState.Charging,
                PointState.Occupied,
                PointState.Unknown,
            }
                .Cast<PointState?>()
                .FirstOrDefault(x => states.Contains(x!.Value))
            ?? (states.Length == 0 ? PointState.Unknown : PointState.OutOfService);
        var status = station.ManualRestriction switch
        {
            ManualRestriction.Maintenance => PointState.Maintenance,
            ManualRestriction.OutOfService => PointState.OutOfService,
            _ => normal,
        };
        var warning = connectors
            .OrderBy(x => x.Number)
            .Select(x => x.DeviceFaultCode ?? x.FaultCode)
            .FirstOrDefault(x => x is not null);
        var now = clock.GetUtcNow();
        var statusChanged =
            station.Status != status
            || station.StatusSource != "Device"
            || station.WarningCode != warning;
        station.Status = status;
        var saved = AvailabilityForecastRules.ForDriver(station, now);
        var forecasts = connectors
            .Where(x =>
                x.IsOnline
                && x.Status == ChargingState.Charging
                && x.FaultCode is (null or "NoError")
                && x.DeviceFaultCode is (null or "NoError")
            )
            .Select(connector =>
            {
                var session = sessions.SingleOrDefault(x => x.ConnectorId == connector.Id);
                if (session is null || session.Status != ChargingState.Charging)
                    return null;
                var supplied =
                    reportedEstimate?.ConnectorId == connector.Number ? reportedEstimate
                    : saved?.ConnectorId == connector.Number
                    && saved.Source is ("Device" or "Simulator")
                        ? saved
                    : null;
                if (supplied is not null)
                {
                    if (
                        supplied.Source == "Simulator"
                        && (
                            session.PowerKW is not > 0
                            || session.PowerScope != "Total"
                            || session.PowerReceivedAt?.AddSeconds(120) <= now
                            || session.PowerReceivedAt is null
                        )
                    )
                        return null;
                    return supplied.EstimatedAvailableAt > now && supplied.ValidUntil > now
                        ? supplied
                        : null;
                }
                return AvailabilityForecastRules.Calculate(session, connector, now);
            });
        var forecast =
            status == PointState.Charging
                ? forecasts
                    .Where(x => x is not null)
                    .OrderBy(x => x!.EstimatedAvailableAt)
                    .FirstOrDefault()
                : null;
        if (
            !statusChanged
            && station.ForecastConnectorId == forecast?.ConnectorId
            && station.ForecastAvailableAt == forecast?.EstimatedAvailableAt
            && station.ForecastObservedAt == forecast?.ObservedAt
            && station.ForecastSource == forecast?.Source
            && station.ForecastValidUntil == forecast?.ValidUntil
        )
            return;
        station.StatusSource = "Device";
        station.WarningCode = warning;
        if (statusChanged)
        {
            station.StatusObservedAt = connectors
                .Select(x => x.StatusObservedAt)
                .DefaultIfEmpty()
                .Max();
            station.StatusReceivedAt = now;
        }
        station.Version++;
        station.ForecastConnectorId = forecast?.ConnectorId;
        station.ForecastAvailableAt = forecast?.EstimatedAvailableAt;
        station.ForecastObservedAt = forecast?.ObservedAt;
        station.ForecastSource = forecast?.Source;
        station.ForecastValidUntil = forecast?.ValidUntil;
        ChargingRealtime.Stage(
            db,
            station.Id,
            "StationChanged",
            "ChargingStation",
            station.Id,
            station.Version,
            now,
            station: ChargingRealtime.StationDto(station, now)
        );
    }

    private static PointState Map(ChargingState status) =>
        status switch
        {
            ChargingState.Available => PointState.Available,
            ChargingState.Charging => PointState.Charging,
            ChargingState.Connecting
            or ChargingState.Finishing
            or ChargingState.Reserved
            or ChargingState.PausedByVehicle
            or ChargingState.PausedByCharger => PointState.Occupied,
            ChargingState.Faulted or ChargingState.Unavailable => PointState.OutOfService,
            _ => PointState.Unknown,
        };
}

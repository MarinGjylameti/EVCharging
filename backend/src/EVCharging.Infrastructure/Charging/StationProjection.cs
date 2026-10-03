using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationProjection(ChargingDbContext db, TimeProvider clock)
{
    public async Task RefreshAsync(Guid stationId, CancellationToken ct)
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
        var active = await db
            .ChargingSessions.Where(x => x.PointId == stationId && x.EndedAt == null)
            .Select(x => x.ConnectorId)
            .ToListAsync(ct);
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
        if (
            station.Status == status
            && station.StatusSource == "Device"
            && station.WarningCode == warning
        )
            return;
        station.Status = status;
        station.StatusSource = "Device";
        station.WarningCode = warning;
        station.StatusObservedAt = connectors
            .Select(x => x.StatusObservedAt)
            .DefaultIfEmpty()
            .Max();
        station.StatusReceivedAt = clock.GetUtcNow();
        station.Version++;
        AvailabilityForecastRules.Clear(station);
        ChargingRealtime.Stage(
            db,
            station.Id,
            "StationChanged",
            "ChargingStation",
            station.Id,
            station.Version,
            clock.GetUtcNow(),
            station: ChargingRealtime.StationDto(station, clock.GetUtcNow())
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

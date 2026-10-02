using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverQueries(
    ChargingDbContext db,
    TimeProvider clock,
    IChargingClaimVerification verification
) : IDriverQueries
{
    public async Task<DriverChargingState> GetChargingStateAsync(
        Guid driverId,
        CancellationToken ct,
        Guid? stationId = null
    )
    {
        // Active view: one claim/session. A charger-detail recovery may also return
        // its latest completed session, without scanning historical telemetry.
        var claim = await db
            .ChargingConnectionRequests.AsNoTracking()
            .Where(x =>
                x.DriverId == driverId
                && x.SessionId != null
                && (
                    stationId == null
                        ? x.State == "Linked"
                        : x.StationId == stationId
                            && (x.State == "Linked" || x.State == "Completed")
                )
            )
            .OrderByDescending(x => x.LinkedAt)
            .FirstOrDefaultAsync(ct);
        if (claim is null)
            return new(verification.DemoBypassEnabled, []);
        var session = await db
            .ChargingSessions.AsNoTracking()
            .SingleAsync(x => x.Id == claim.SessionId, ct);
        var connector = await db
            .ChargingConnectors.AsNoTracking()
            .SingleAsync(x => x.Id == session.ConnectorId, ct);
        var station = await db
            .Stations.AsNoTracking()
            .SingleAsync(x => x.Id == session.PointId, ct);
        return new(
            verification.DemoBypassEnabled,
            [ChargingRealtime.SessionDto(session, connector, station, clock.GetUtcNow())]
        );
    }

    public async Task<IReadOnlyList<DriverStationDto>> GetStationsAsync(CancellationToken ct)
    {
        var stations = await db
            .Stations.AsNoTracking()
            .Where(x => !x.IsArchived)
            .OrderBy(x => x.City)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        return stations.Select(x => ChargingRealtime.StationDto(x, clock.GetUtcNow())).ToArray();
    }

    public async Task<IReadOnlyList<DriverHistoryDto>> GetHistoryAsync(
        Guid driverId,
        CancellationToken ct
    ) =>
        await db
            .ChargingHistory.AsNoTracking()
            .Where(x => x.DriverId == driverId)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .Select(x => new DriverHistoryDto(
                x.Id,
                x.StartedAt,
                x.EndedAt,
                x.EnergyKWh,
                x.UnitPriceSnapshot,
                x.TotalCost,
                x.Currency,
                x.IsDemo,
                x.StationId,
                x.Station.Code,
                x.Station.Name,
                x.Station.City
            ))
            .ToListAsync(ct);
}

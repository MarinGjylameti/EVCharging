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
        // All active assignments, or one latest owned session per connector on detail.
        var query = db
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
            );
        if (stationId is not null)
            query = query
                .GroupBy(x => x.ConnectorNumber)
                .Select(g =>
                    g.OrderByDescending(x => x.State == "Linked")
                        .ThenByDescending(x => x.LinkedAt)
                        .ThenByDescending(x => x.CreatedAt)
                        .First()
                );
        var claims = (await query.ToListAsync(ct))
            .OrderByDescending(x => x.State == "Linked")
            .ThenByDescending(x => x.LinkedAt)
            .ToList();
        if (claims.Count == 0)
            return new(verification.DemoBypassEnabled, []);
        var ids = claims.Select(x => x.SessionId).ToArray();
        var sessions = await db
            .ChargingSessions.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var connectorIds = sessions.Values.Select(x => x.ConnectorId).ToArray();
        var connectors = await db
            .ChargingConnectors.AsNoTracking()
            .Where(x => connectorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var stationIds = sessions.Values.Select(x => x.PointId).ToArray();
        var stations = await db
            .Stations.AsNoTracking()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return new(
            verification.DemoBypassEnabled,
            claims
                .Select(c => sessions[c.SessionId!.Value])
                .Select(s =>
                    ChargingRealtime.SessionDto(
                        s,
                        connectors[s.ConnectorId],
                        stations[s.PointId],
                        clock.GetUtcNow()
                    )
                )
                .ToArray()
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

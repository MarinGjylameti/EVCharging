using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverQueries(ChargingDbContext db, TimeProvider clock) : IDriverQueries
{
    public async Task<IReadOnlyList<DriverStationDto>> GetStationsAsync(CancellationToken ct)
    {
        var stations = await db
            .Stations.AsNoTracking()
            .Where(station => !station.IsArchived)
            .OrderBy(station => station.City)
            .ThenBy(station => station.Name)
            .ToListAsync(ct);
        var now = clock.GetUtcNow();

        return stations
            .Select(station => new DriverStationDto(
                station.Id,
                station.Code,
                station.Name,
                station.City,
                station.Address,
                station.Latitude,
                station.Longitude,
                station.ConnectorType,
                station.ConnectorCount,
                station.PowerKW,
                station.PricePerKWh,
                station.Currency,
                station.Status.ToString(),
                station.IsDemo,
                station.Version,
                AvailabilityForecastRules.ForDriver(station, now),
                station.StatusSource,
                station.StatusObservedAt,
                station.StatusReceivedAt
            ))
            .ToArray();
    }

    public async Task<IReadOnlyList<DriverHistoryDto>> GetHistoryAsync(
        Guid driverId,
        CancellationToken ct
    )
    {
        return await db
            .ChargingHistory.AsNoTracking()
            .Where(history => history.DriverId == driverId)
            .OrderByDescending(history => history.StartedAt)
            .ThenByDescending(history => history.Id)
            .Take(100)
            .Select(history => new DriverHistoryDto(
                history.Id,
                history.StartedAt,
                history.EndedAt,
                history.EnergyKWh,
                history.UnitPriceSnapshot,
                history.TotalCost,
                history.Currency,
                history.IsDemo,
                history.StationId,
                history.Station.Code,
                history.Station.Name,
                history.Station.City
            ))
            .ToListAsync(ct);
    }
}

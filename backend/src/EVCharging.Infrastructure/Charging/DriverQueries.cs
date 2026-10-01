using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverQueries(
    ChargingDbContext db,
    TimeProvider clock,
    IConfiguration configuration,
    IOptions<OcppOptions> options
) : IDriverQueries
{
    public async Task<DriverChargingState> GetChargingStateAsync(
        Guid driverId,
        CancellationToken ct
    )
    {
        var email = configuration["Demo:DriverEmail"]?.Trim().ToUpperInvariant();
        var canTest =
            options.Value.SimulatorEnabled
            && email is not null
            && await db.Accounts.AnyAsync(x => x.Id == driverId && x.NormalizedEmail == email, ct);
        var transactions = await db
            .OcppChargingTransactions.AsNoTracking()
            .Where(x => x.DriverId == driverId && x.StationId != null)
            .OrderByDescending(x => x.StartReceivedAt)
            .Take(100)
            .ToListAsync(ct);
        var latest = transactions
            .GroupBy(x => x.StationId)
            .Select(group => group.First())
            .ToArray();
        var stationIds = latest.Select(x => x.StationId!.Value).ToArray();
        var stations = await db
            .Stations.AsNoTracking()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var connectors = await db
            .OcppConnectors.AsNoTracking()
            .Where(x => x.StationId != null && stationIds.Contains(x.StationId.Value))
            .ToListAsync(ct);
        var result = new List<DriverChargingSession>();
        foreach (var transaction in latest)
        {
            if (!stations.TryGetValue(transaction.StationId!.Value, out var station))
                continue;
            var connector = connectors.SingleOrDefault(x =>
                x.ChargePointId == transaction.ChargePointId
                && x.ConnectorNumber == transaction.ConnectorNumber
            );
            var status =
                transaction.StoppedAt is not null ? "Stopped"
                : station.Status == PointState.Unknown ? "Disconnected"
                : connector?.ReportedStatus ?? "Preparing";
            var samples = await db
                .OcppMeterSamples.AsNoTracking()
                .Where(x =>
                    x.TransactionId == transaction.Id
                    && (x.Measurand == "SoC" || x.Measurand == "Temperature")
                    && x.NumericValue != null
                    && x.Format != "SignedData"
                    && x.Phase == null
                    && x.ObservedAt >= transaction.StartedAt
                    && x.ObservedAt <= clock.GetUtcNow().AddMinutes(5)
                )
                .OrderByDescending(x => x.ObservedAt)
                .Take(20)
                .ToListAsync(ct);
            result.Add(
                new DriverChargingSession(
                    transaction.Id,
                    station.Id,
                    station.Name,
                    station.Code,
                    transaction.ConnectorNumber,
                    status,
                    transaction.EnergyKWh ?? 0,
                    status == "Disconnected" ? null : transaction.CurrentPowerKW,
                    samples
                        .FirstOrDefault(x =>
                            x.Measurand == "SoC"
                            && x.NumericValue is >= 0 and <= 100
                            && x.Unit is (null or "Percent")
                        )
                        ?.NumericValue,
                    samples
                        .FirstOrDefault(x =>
                            x.Measurand == "Temperature" && x.Unit is null or "Celsius"
                        )
                        ?.NumericValue,
                    transaction.StartedAt,
                    transaction.StoppedAt,
                    transaction.LastMeasurementAt,
                    transaction.CalculatedCost,
                    transaction.Currency ?? "EUR",
                    station.IsDemo
                )
            );
        }
        return new DriverChargingState(canTest, result);
    }

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

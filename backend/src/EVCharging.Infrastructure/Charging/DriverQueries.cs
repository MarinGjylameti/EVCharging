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
        CancellationToken ct,
        Guid? stationId = null
    )
    {
        var email = configuration["Demo:DriverEmail"]?.Trim().ToUpperInvariant();
        var canTest =
            options.Value.SimulatorEnabled
            && email is not null
            && await db.Accounts.AnyAsync(x => x.Id == driverId && x.NormalizedEmail == email, ct);
        var transactions = await db
            .OcppChargingTransactions.AsNoTracking()
            .Where(x =>
                x.DriverId == driverId
                && x.StationId != null
                && (stationId == null || x.StationId == stationId)
            )
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
        var deviceIds = latest.Select(x => x.ChargePointId).Distinct().ToArray();
        var devices = await db
            .OcppChargePoints.AsNoTracking()
            .Where(x => deviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var now = clock.GetUtcNow();
        var liveCutoff = now.AddSeconds(-options.Value.OfflineSeconds);
        var result = new List<DriverChargingSession>();
        foreach (var transaction in latest)
        {
            if (!stations.TryGetValue(transaction.StationId!.Value, out var station))
                continue;
            var simulationClock = station.IsDemo && options.Value.SimulatorEnabled;
            var latestAllowedTime = simulationClock ? now.AddDays(1) : now.AddMinutes(5);
            var connector = connectors.SingleOrDefault(x =>
                x.ChargePointId == transaction.ChargePointId
                && x.ConnectorNumber == transaction.ConnectorNumber
            );
            var status =
                transaction.StoppedAt is not null ? "Stopped"
                : !devices.TryGetValue(transaction.ChargePointId, out var device)
                || !device.IsEnabled
                || device.LastSeenAt is null
                || device.LastSeenAt < liveCutoff
                    ? "Disconnected"
                : connector?.ReportedStatus ?? "AwaitingStatus";
            var samples = await db
                .OcppMeterSamples.AsNoTracking()
                .Where(x =>
                    x.TransactionId == transaction.Id
                    && x.NumericValue != null
                    && x.Format != "SignedData"
                    && x.ObservedAt >= transaction.StartedAt
                    && x.ObservedAt <= latestAllowedTime
                )
                .OrderByDescending(x => x.ObservedAt)
                .ThenByDescending(x => x.ReceivedAt)
                .Take(256)
                .ToListAsync(ct);
            // Also interpret already stored phase readings from before this fix.
            // Reading the snapshot does not mutate the original telemetry or history.
            var readings = OcppMeterReading.FromSamples(
                samples,
                transaction.StartedAt,
                now,
                simulationClock
            );
            var energyReading = readings.LastOrDefault(x =>
                x.Measurand == "Energy.Active.Import.Register"
            );
            var powerReading = readings.LastOrDefault(x => x.Measurand == "Power.Active.Import");
            var energy = transaction.StoppedAt is not null
                ? transaction.EnergyKWh
                : energyReading?.SessionEnergy(transaction.MeterStartWh);
            if (transaction.StoppedAt is null && energyReading is null)
                energy = transaction.EnergyKWh;
            else if (
                transaction.StoppedAt is null
                && energy is not null
                && transaction.EnergyKWh > energy
            )
                energy = transaction.EnergyKWh;
            var lastReading = readings.LastOrDefault();
            var clockSkewed =
                (transaction.StartReceivedAt - transaction.StartedAt).Duration()
                    > TimeSpan.FromMinutes(5)
                || (
                    lastReading is not null
                    && (lastReading.ReceivedAt - lastReading.ObservedAt).Duration()
                        > TimeSpan.FromMinutes(5)
                );
            result.Add(
                new DriverChargingSession(
                    transaction.Id,
                    station.Id,
                    station.Name,
                    station.Code,
                    transaction.ConnectorNumber,
                    status,
                    energy,
                    status == "Disconnected" ? null
                        : transaction.StoppedAt is not null ? 0
                        : powerReading?.Value ?? transaction.CurrentPowerKW,
                    samples
                        .FirstOrDefault(x =>
                            x.Measurand == "SoC"
                            && x.Phase == null
                            && x.NumericValue is >= 0 and <= 100
                            && x.Unit is (null or "Percent")
                        )
                        ?.NumericValue,
                    samples
                        .FirstOrDefault(x =>
                            x.Measurand == "Temperature"
                            && x.Phase == null
                            && x.Unit is null or "Celsius"
                        )
                        ?.NumericValue,
                    transaction.StartedAt,
                    transaction.StoppedAt,
                    lastReading?.ObservedAt ?? transaction.LastMeasurementAt,
                    transaction.StoppedAt is not null ? transaction.CalculatedCost
                        : energy is not null && transaction.UnitPriceSnapshot is { } price
                            ? decimal.Round(energy.Value * price, 2, MidpointRounding.AwayFromZero)
                        : null,
                    transaction.Currency ?? "EUR",
                    station.IsDemo,
                    transaction.StartReceivedAt,
                    transaction.StopReceivedAt,
                    lastReading?.ReceivedAt,
                    clockSkewed,
                    transaction.StoppedAt is not null ? "Total" : energyReading?.Scope,
                    powerReading?.Scope,
                    transaction.UnitPriceSnapshot
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

using EVCharging.Domain.Charging;

namespace EVCharging.Application.Charging;

public sealed record DriverStationDto(
    Guid Id,
    string Code,
    string Name,
    string City,
    string Address,
    double Latitude,
    double Longitude,
    string ConnectorType,
    int ConnectorCount,
    decimal PowerKW,
    decimal PricePerKWh,
    string Currency,
    string Status,
    bool IsDemo,
    long Version,
    AvailabilityForecast? AvailabilityForecast,
    string? StatusSource,
    DateTimeOffset? StatusObservedAt,
    DateTimeOffset? StatusReceivedAt
);

public sealed record DriverHistoryDto(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    decimal EnergyKWh,
    decimal UnitPriceSnapshot,
    decimal TotalCost,
    string Currency,
    bool IsDemo,
    Guid StationId,
    string StationCode,
    string StationName,
    string City
);

public interface IDriverQueries
{
    Task<DriverChargingState> GetChargingStateAsync(
        Guid driverId,
        CancellationToken ct,
        Guid? stationId = null
    );
    Task<IReadOnlyList<DriverStationDto>> GetStationsAsync(CancellationToken ct);
    Task<IReadOnlyList<DriverHistoryDto>> GetHistoryAsync(Guid driverId, CancellationToken ct);
}

public sealed record DriverChargingState(
    bool CanTestSimulator,
    IReadOnlyList<DriverChargingSession> Sessions
);

public sealed record DriverChargingSession(
    Guid Id,
    Guid StationId,
    string StationName,
    string StationCode,
    int ConnectorId,
    string Status,
    decimal? EnergyKWh,
    decimal? PowerKW,
    decimal? StateOfCharge,
    decimal? TemperatureCelsius,
    DateTimeOffset StartedAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? LastMeasurementAt,
    decimal? Cost,
    string Currency,
    bool IsDemo,
    DateTimeOffset StartReceivedAt,
    DateTimeOffset? StopReceivedAt,
    DateTimeOffset? LastMeasurementReceivedAt,
    bool DeviceClockSkewed,
    string? EnergyScope,
    string? PowerScope,
    decimal? UnitPricePerKWh
);

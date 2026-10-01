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
    Task<IReadOnlyList<DriverStationDto>> GetStationsAsync(CancellationToken ct);
    Task<IReadOnlyList<DriverHistoryDto>> GetHistoryAsync(Guid driverId, CancellationToken ct);
}

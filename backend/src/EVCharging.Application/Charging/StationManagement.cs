using EVCharging.Application.Common;
using EVCharging.Domain.Charging;

namespace EVCharging.Application.Charging;

public sealed record StationInput(
    string? Code,
    string? Name,
    string? City,
    string? Address,
    double Latitude,
    double Longitude,
    string? ConnectorType,
    int ConnectorCount,
    decimal PowerKW,
    decimal PricePerKWh,
    string? Status,
    long Version = 0,
    Guid? LocationId = null,
    string? Reason = null
);

public sealed record ArchiveStationInput(long Version);

public sealed record StationDto(
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
    string? ManualRestriction,
    bool IsArchived,
    bool IsDemo,
    long Version,
    string? StatusSource,
    DateTimeOffset? StatusObservedAt,
    DateTimeOffset? StatusReceivedAt,
    Guid? LocationId = null
)
{
    public static StationDto From(ChargingStation station) =>
        new(
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
            station.ManualRestriction?.ToString(),
            station.IsArchived,
            station.IsDemo,
            station.Version,
            station.StatusSource,
            station.StatusObservedAt,
            station.StatusReceivedAt,
            station.LocationId
        );
}

public interface IStationManagement
{
    Task<IReadOnlyList<StationDto>> ListAsync(Guid actorId, CancellationToken ct);
    Task<StationDto> CreateAsync(StationInput input, Guid actorId, CancellationToken ct);
    Task<StationDto> UpdateAsync(Guid id, StationInput input, Guid actorId, CancellationToken ct);
    Task<StationDto> ArchiveAsync(
        Guid id,
        ArchiveStationInput input,
        Guid actorId,
        CancellationToken ct
    );
}

public sealed record ValidatedStation(
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
    PointState Status
);

public static class StationRules
{
    public static ValidatedStation Validate(StationInput input)
    {
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var name = input.Name?.Trim() ?? "";
        var city = input.City?.Trim() ?? "";
        var address = input.Address?.Trim() ?? "";
        var connector = input.ConnectorType?.Trim() ?? "";

        if (
            code.Length is < 2 or > 40
            || !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-')
        )
            throw new RequestFailure(
                400,
                "invalid_station_code",
                "Use 2–40 uppercase letters, digits or hyphens."
            );

        if (
            name.Length is < 2 or > 120
            || city.Length is < 2 or > 80
            || address.Length is < 2 or > 200
            || connector.Length is < 2 or > 40
        )
            throw new RequestFailure(
                400,
                "invalid_station_details",
                "Station name, city, address or connector is invalid."
            );

        if (
            !double.IsFinite(input.Latitude)
            || !double.IsFinite(input.Longitude)
            || input.Latitude is < -90 or > 90
            || input.Longitude is < -180 or > 180
        )
            throw new RequestFailure(400, "invalid_coordinates", "Coordinates are invalid.");

        if (
            input.ConnectorCount is < 1 or > 100
            || input.PowerKW is <= 0 or > 99999999m
            || input.PricePerKWh is < 0 or > 10000m
            || decimal.Round(input.PowerKW, 2) != input.PowerKW
            || decimal.Round(input.PricePerKWh, 4) != input.PricePerKWh
        )
            throw new RequestFailure(
                400,
                "invalid_station_capacity",
                "Connector count, power or price is invalid."
            );

        if (
            !Enum.TryParse<PointState>(input.Status, false, out var status)
            || !Enum.IsDefined(status)
        )
            throw new RequestFailure(400, "invalid_station_status", "Station status is invalid.");

        return new ValidatedStation(
            code,
            name,
            city,
            address,
            input.Latitude,
            input.Longitude,
            connector,
            input.ConnectorCount,
            input.PowerKW,
            input.PricePerKWh,
            status
        );
    }
}

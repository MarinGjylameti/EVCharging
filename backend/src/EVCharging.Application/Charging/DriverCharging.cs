namespace EVCharging.Application.Charging;

public sealed record ChargingConnectionDto(Guid Id, string State, DateTimeOffset ExpiresAt);

public sealed record DriverStationCharging(
    DriverStationDto Station,
    bool CanConnect,
    bool DeviceConnected,
    bool InUse,
    ChargingConnectionDto? ConnectionRequest,
    DriverChargingSession? Session,
    DriverConnectorState? Connector = null,
    ChargingDiagnostics? Diagnostics = null
);

public interface IDriverCharging
{
    Task<DriverStationCharging> GetAsync(Guid driverId, Guid stationId, CancellationToken ct);
    Task<DriverStationCharging> ConnectAsync(Guid driverId, Guid stationId, CancellationToken ct);
    Task<DriverStationCharging> CancelAsync(Guid driverId, Guid stationId, CancellationToken ct);
}

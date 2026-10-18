namespace EVCharging.Application.Charging;

public sealed record ChargingConnectionDto(Guid Id, string State, DateTimeOffset ExpiresAt);

public sealed record BlockingChargingConnection(
    Guid StationId,
    string StationName,
    int ConnectorId,
    string State,
    DateTimeOffset ExpiresAt
);

public sealed record DriverStationCharging(
    DriverStationDto Station,
    bool CanConnect,
    bool DeviceConnected,
    bool InUse,
    ChargingConnectionDto? ConnectionRequest,
    DriverChargingSession? Session,
    DriverConnectorState? Connector = null,
    ChargingDiagnostics? Diagnostics = null,
    string? DriverAccessMode = null,
    BlockingChargingConnection? BlockingConnection = null
);

public interface IDriverCharging
{
    Task<DriverStationCharging> GetAsync(
        Guid driverId,
        Guid stationId,
        CancellationToken ct,
        int? connectorNumber = null
    );
    Task<DriverStationCharging> ConnectAsync(Guid driverId, Guid stationId, CancellationToken ct);
    Task<DriverStationCharging> CancelAsync(Guid driverId, Guid stationId, CancellationToken ct);
}

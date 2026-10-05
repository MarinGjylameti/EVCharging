namespace EVCharging.Application.Charging;

// Optional test diagnostics. Runtime session contracts remain independent of the transport.
public sealed record ChargingDiagnostics(
    string? ExternalSessionId,
    long? MeterStartWh,
    string? StopReason,
    ChargingCommandResult? LastCommand,
    IReadOnlyList<ChargingSampleDiagnostic> Samples,
    ChargingCommandResult? LastFailure = null
);

public sealed record ChargingCommandResult(
    string Action,
    bool Accepted,
    string? FailureCode,
    string? Description,
    DateTimeOffset ReceivedAt,
    long Version = 0,
    IReadOnlyList<ChargingSampleDiagnostic>? Readings = null
);

public sealed record ChargingSampleDiagnostic(
    string Measurand,
    string Value,
    string? Unit,
    string? Phase,
    string? Location,
    string? Context,
    string? Format,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt
);

public sealed record DriverConnectorState(
    string Status,
    string? FaultCode,
    string? FaultInfo,
    long Version = 0,
    string? DeviceStatus = null,
    string? DeviceFaultCode = null,
    string? DeviceFaultInfo = null,
    int Number = 1
);

public interface IChargingDiagnostics
{
    Task<ChargingDiagnostics?> GetAsync(
        Guid connectorId,
        Guid? sessionId,
        CancellationToken ct,
        DateTimeOffset? since = null
    );
}

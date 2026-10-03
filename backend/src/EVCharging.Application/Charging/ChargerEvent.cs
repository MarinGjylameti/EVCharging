using EVCharging.Domain.Charging;

namespace EVCharging.Application.Charging;

public enum ChargerEventKind
{
    Connected,
    Started,
    Measurement,
    Stopped,
    Disconnected,
    Faulted,
    StatusReported,
}

public sealed record ChargerEvent(
    string ProviderId,
    string EventId,
    string DeviceReference,
    string ConnectorReference,
    string? ExternalSessionId,
    ChargerEventKind Kind,
    DateTimeOffset ObservedAt,
    decimal? SessionEnergyKWh,
    decimal? PowerKW,
    string? VerifiedDriverCorrelation,
    Guid? SessionId = null,
    Guid? ConnectorId = null,
    DateTimeOffset? ReceivedAt = null,
    ChargingState? Status = null,
    decimal? StateOfCharge = null,
    decimal? TemperatureCelsius = null,
    string? EnergyScope = null,
    string? PowerScope = null,
    string? FaultCode = null,
    string? FaultInfo = null,
    bool UseReceiptOrder = false,
    IReadOnlyList<ChargingSampleDiagnostic>? Readings = null,
    string? StopReason = null,
    string? CompletionWarning = null,
    string? DeviceStatus = null,
    string? DeviceFaultCode = null,
    string? DeviceFaultInfo = null
);

// Adapters authenticate and normalize their own protocol. UI clients cannot implement this port.
public interface IChargerEventReceiver
{
    Task ReceiveAsync(ChargerEvent chargerEvent, CancellationToken cancellationToken);
}

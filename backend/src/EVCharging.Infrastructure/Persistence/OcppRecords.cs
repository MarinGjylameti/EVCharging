namespace EVCharging.Infrastructure.Persistence;

// OCPP transport data stays in Infrastructure. The public station and driver models
// do not depend on a particular protocol version or a vendor-specific payload.
public sealed class OcppChargePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Identity { get; set; } = "";
    public string ProtocolVersion { get; set; } = "1.6J";
    public bool IsEnabled { get; set; }
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? ChargePointSerialNumber { get; set; }
    public string? ChargeBoxSerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Iccid { get; set; }
    public string? Imsi { get; set; }
    public string? MeterType { get; set; }
    public string? MeterSerialNumber { get; set; }
    public DateTimeOffset? LastBootAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
}

public sealed class OcppConnector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public int ConnectorNumber { get; set; }

    // A physical connector can be provisioned before its public station is known.
    public Guid? StationId { get; set; }
    public Guid? BusinessConnectorId { get; set; }
    public string? ReportedStatus { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? StatusObservedAt { get; set; }
    public DateTimeOffset? StatusReceivedAt { get; set; }
    public long Version { get; set; }
}

public sealed class OcppConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public string Subprotocol { get; set; } = "ocpp1.6";
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset? DisconnectedAt { get; set; }
    public DateTimeOffset? LastFrameAt { get; set; }
    public int? CloseCode { get; set; }
    public string? CloseReason { get; set; }
}

public sealed class OcppInboundMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public Guid ConnectionId { get; set; }

    // OCPP's call ID is unique within a connection, not a durable event ID.
    public string CallId { get; set; } = "";
    public string Action { get; set; } = "";
    public string FrameJson { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string? ResponseJson { get; set; }
    public string ProcessingState { get; set; } = "Received";
    public string? FailureCode { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class OcppStatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public Guid MessageId { get; set; }

    // Connector 0 denotes the whole charge point in OCPP 1.6.
    public int ConnectorNumber { get; set; }
    public string Status { get; set; } = "";
    public string ErrorCode { get; set; } = "";
    public string? Info { get; set; }
    public string? VendorId { get; set; }
    public string? VendorErrorCode { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class OcppChargingTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public int ConnectorNumber { get; set; }
    public Guid? StationId { get; set; }

    public Guid? SessionId { get; set; }
    public int Ocpp16TransactionId { get; set; }
    public string? ExternalTransactionId { get; set; }
    public Guid StartMessageId { get; set; }
    public Guid? StopMessageId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset StartReceivedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public DateTimeOffset? StopReceivedAt { get; set; }
    public DateTimeOffset? PhysicalClosedAt { get; set; }
    public string State { get; set; } = "Active";
    public string? StopReason { get; set; }

    // OCPP 1.6 meterStart/meterStop are integral Wh. Keep their source precision.
    public long MeterStartWh { get; set; }
    public long? MeterStopWh { get; set; }
}

public sealed class OcppMeterSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChargePointId { get; set; }
    public Guid MessageId { get; set; }
    public Guid? TransactionId { get; set; }
    public int ConnectorNumber { get; set; }
    public int MeterValueIndex { get; set; }
    public int SampleIndex { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Measurand { get; set; } = "Energy.Active.Import.Register";
    public string ValueText { get; set; } = "";
    public decimal? NumericValue { get; set; }
    public string? Unit { get; set; }
    public string? Phase { get; set; }
    public string? Location { get; set; }
    public string? Context { get; set; }
    public string? Format { get; set; }
}

// Persisted with a station/session update; a dispatcher may publish it only
// after the enclosing database transaction commits.
public sealed class RealtimeOutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public long EntityVersion { get; set; }
    public string Audience { get; set; } = "PublicStation";
    public Guid? RecipientAccountId { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastFailureCode { get; set; }
}

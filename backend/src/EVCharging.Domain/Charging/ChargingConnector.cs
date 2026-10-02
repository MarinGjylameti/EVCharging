namespace EVCharging.Domain.Charging;

public enum ChargingState
{
    Unknown, Available, Connecting, Charging, PausedByVehicle, PausedByCharger,
    Finishing, Reserved, Faulted, Unavailable, Completed,
}

// Business connector identity and latest projection, independent of transport.
public sealed class ChargingConnector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StationId { get; set; }
    public int Number { get; set; }
    public ChargingState Status { get; set; } = ChargingState.Unknown;
    public bool IsOnline { get; set; }
    public DateTimeOffset? StatusObservedAt { get; set; }
    public DateTimeOffset? StatusReceivedAt { get; set; }
    public long Version { get; set; }
}

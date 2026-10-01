namespace EVCharging.Domain.Charging;

// The driver's request to use a connector is separate from the physical session.
// Opening the page never creates a billable charging session.
public sealed class ChargingConnectionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DriverId { get; set; }
    public Guid StationId { get; set; }
    public int ConnectorNumber { get; set; } = 1;
    public string State { get; set; } = "Waiting";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LinkedAt { get; set; }
    public Guid? SessionId { get; set; }
}

namespace EVCharging.Domain.Charging;

public sealed class ChargingLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsArchived { get; set; }
    public long Version { get; set; } = 1;
}

// A location grant covers its stations and permits creation there. A station
// grant covers only that station and never grants access to its neighbours.
public sealed class AdministratorGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdministratorId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? StationId { get; set; }
}

public sealed class PriceRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StationId { get; set; }
    public Guid ActorId { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal PricePerKWh { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Reason { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

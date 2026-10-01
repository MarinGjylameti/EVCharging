namespace EVCharging.Domain.Charging;

// Read models for the driver map and history. Demo rows are seeded only by BrowserTestHost.
public sealed class ChargingStation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string ConnectorType { get; set; } = "";
    public int ConnectorCount { get; set; }
    public decimal PowerKW { get; set; }
    public decimal PricePerKWh { get; set; }
    public string Currency { get; set; } = "EUR";
    public PointState Status { get; set; } = PointState.Available;
    // Supplied by a verified device adapter when supported. Never inferred from charging power alone.
    public int? ForecastConnectorId { get; set; }
    public DateTimeOffset? ForecastAvailableAt { get; set; }
    public DateTimeOffset? ForecastObservedAt { get; set; }
    public string? ForecastSource { get; set; }
    public bool IsArchived { get; set; }
    public bool IsDemo { get; set; }
    public long Version { get; set; }
}

public sealed class ChargingHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DriverId { get; set; }
    public Guid StationId { get; set; }
    public ChargingStation Station { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public decimal EnergyKWh { get; set; }
    public decimal UnitPriceSnapshot { get; set; }
    public decimal TotalCost { get; set; }
    public string Currency { get; set; } = "EUR";
    public bool IsDemo { get; set; }
}

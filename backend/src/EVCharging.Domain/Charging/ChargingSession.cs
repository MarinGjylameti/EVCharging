namespace EVCharging.Domain.Charging;

public sealed class ChargingSession
{
    private readonly HashSet<string> processed = new(StringComparer.Ordinal);
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PointId { get; set; }
    public Guid ConnectorId { get; set; }
    public int ConnectorNumber { get; set; } = 1;
    public decimal UnitPriceSnapshot { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset StartReceivedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? StopReceivedAt { get; set; }
    public decimal? EnergyKWh { get; set; } = 0;
    public decimal? PowerKW { get; set; }
    public decimal? StateOfCharge { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public DateTimeOffset LastMeasurementAt { get; set; }
    public DateTimeOffset? LastMeasurementReceivedAt { get; set; }
    public DateTimeOffset? EnergyObservedAt { get; set; }
    public DateTimeOffset? PowerObservedAt { get; set; }
    public DateTimeOffset? PowerReceivedAt { get; set; }
    public DateTimeOffset? SocObservedAt { get; set; }
    public DateTimeOffset? TemperatureObservedAt { get; set; }
    public string? EnergyScope { get; set; }
    public string? PowerScope { get; set; }
    public string? LatestReadingsJson { get; set; }
    public string? StopReason { get; set; }
    public string? CompletionWarning { get; set; }
    public ChargingState Status { get; set; } = ChargingState.Connecting;
    public long Version { get; set; }
    public bool IsDemo { get; set; }
    public decimal? CalculatedCost =>
        EnergyKWh is { } energy
            ? decimal.Round(energy * UnitPriceSnapshot, 2, MidpointRounding.AwayFromZero)
            : null;

    public ChargingSession() { }

    public ChargingSession(
        Guid pointId,
        decimal unitPrice,
        string currency,
        DateTimeOffset startedAt
    )
    {
        if (pointId == Guid.Empty)
            throw new ArgumentException("Point is required.");
        if (unitPrice < 0 || unitPrice > 10000)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency is required.");
        PointId = pointId;
        UnitPriceSnapshot = unitPrice;
        Currency = currency;
        StartedAt = LastMeasurementAt = startedAt;
    }

    public bool Measure(
        string eventId,
        decimal sessionEnergyKWh,
        decimal? powerKW,
        DateTimeOffset observedAt
    )
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event ID is required.");
        if (processed.Contains(eventId))
            return false;
        if (EndedAt is not null)
            throw new InvalidOperationException("The session is complete.");
        if (sessionEnergyKWh < 0 || sessionEnergyKWh > 1_000_000 || powerKW is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(sessionEnergyKWh));
        if (observedAt < LastMeasurementAt)
            return false;
        if (sessionEnergyKWh < EnergyKWh)
            throw new InvalidOperationException("Energy cannot decrease.");
        processed.Add(eventId);
        EnergyKWh = sessionEnergyKWh;
        PowerKW = powerKW;
        LastMeasurementAt = observedAt;
        return true;
    }

    public bool Complete(string eventId, decimal finalEnergyKWh, DateTimeOffset endedAt)
    {
        if (processed.Contains(eventId))
            return false;
        if (endedAt < StartedAt)
            throw new InvalidOperationException("End precedes start.");
        if (!Measure(eventId, finalEnergyKWh, null, endedAt))
            return false;
        EndedAt = endedAt;
        return true;
    }
}

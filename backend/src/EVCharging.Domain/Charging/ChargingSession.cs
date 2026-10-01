namespace EVCharging.Domain.Charging;

public sealed class ChargingSession
{
    private readonly HashSet<string> processed = new(StringComparer.Ordinal);
    public Guid Id { get; } = Guid.NewGuid();
    public Guid PointId { get; }
    public decimal UnitPriceSnapshot { get; }
    public string Currency { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; private set; }
    public decimal EnergyKWh { get; private set; }
    public decimal? PowerKW { get; private set; }
    public DateTimeOffset LastMeasurementAt { get; private set; }
    public decimal CalculatedCost => decimal.Round(EnergyKWh * UnitPriceSnapshot, 2, MidpointRounding.AwayFromZero);

    public ChargingSession(Guid pointId, decimal unitPrice, string currency, DateTimeOffset startedAt)
    {
        if (pointId == Guid.Empty) throw new ArgumentException("Point is required.");
        if (unitPrice < 0 || unitPrice > 10000) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z')) throw new ArgumentException("Currency is required.");
        PointId = pointId;
        UnitPriceSnapshot = unitPrice;
        Currency = currency;
        StartedAt = LastMeasurementAt = startedAt;
    }

    public bool Measure(string eventId, decimal sessionEnergyKWh, decimal? powerKW, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("Event ID is required.");
        if (processed.Contains(eventId)) return false;
        if (EndedAt is not null) throw new InvalidOperationException("The session is complete.");
        if (sessionEnergyKWh < 0 || sessionEnergyKWh > 1_000_000 || powerKW is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(sessionEnergyKWh));
        if (observedAt < LastMeasurementAt) return false;
        if (sessionEnergyKWh < EnergyKWh) throw new InvalidOperationException("Energy cannot decrease.");
        processed.Add(eventId);
        EnergyKWh = sessionEnergyKWh;
        PowerKW = powerKW;
        LastMeasurementAt = observedAt;
        return true;
    }

    public bool Complete(string eventId, decimal finalEnergyKWh, DateTimeOffset endedAt)
    {
        if (processed.Contains(eventId)) return false;
        if (endedAt < StartedAt) throw new InvalidOperationException("End precedes start.");
        if (!Measure(eventId, finalEnergyKWh, null, endedAt)) return false;
        EndedAt = endedAt;
        return true;
    }
}

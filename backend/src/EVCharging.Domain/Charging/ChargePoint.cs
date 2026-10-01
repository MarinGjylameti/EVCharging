namespace EVCharging.Domain.Charging;

public enum PointState
{
    Available,
    Occupied,
    Charging,
    OutOfService,
    Maintenance,
    Unknown,
}

public enum ManualRestriction
{
    None,
    OutOfService,
    Maintenance,
}

public sealed class ChargePoint
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public PointState? ReportedState { get; private set; }
    public ManualRestriction Restriction { get; private set; }
    public DateTimeOffset? LastObservedAt { get; private set; }
    public bool IsArchived { get; private set; }
    public long Version { get; private set; }
    public PointState? EffectiveState =>
        Restriction switch
        {
            ManualRestriction.Maintenance => PointState.Maintenance,
            ManualRestriction.OutOfService => PointState.OutOfService,
            _ => ReportedState,
        };

    public bool IsAvailable(DateTimeOffset now, TimeSpan freshness) =>
        !IsArchived
        && EffectiveState == PointState.Available
        && LastObservedAt is { } observed
        && observed <= now
        && now - observed <= freshness;

    public bool Report(PointState state, DateTimeOffset observedAt)
    {
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (LastObservedAt is { } last && observedAt <= last)
            return false;
        ReportedState = state;
        LastObservedAt = observedAt;
        Version++;
        return true;
    }

    public void Restrict(ManualRestriction restriction, string reason)
    {
        if (IsArchived)
            throw new InvalidOperationException("Point is archived.");
        if (!Enum.IsDefined(restriction))
            throw new ArgumentOutOfRangeException(nameof(restriction));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required.", nameof(reason));
        Restriction = restriction;
        Version++;
    }

    public void Archive(bool hasOpenPhysicalSession)
    {
        if (hasOpenPhysicalSession || ReportedState is PointState.Charging or PointState.Occupied)
            throw new InvalidOperationException("An occupied point cannot be archived.");
        IsArchived = true;
        Version++;
    }
}

namespace EVCharging.Application.Charging;

public sealed record AnalyticsFilter(
    string Period = "Daily",
    DateOnly? Date = null,
    DateOnly? From = null,
    DateOnly? Until = null,
    string TimeZone = "Europe/Tirane"
);

public sealed record AnalyticsPeriod(
    string Kind,
    string TimeZone,
    DateOnly Today,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset From,
    DateTimeOffset Until,
    DateTimeOffset EffectiveUntil,
    DateTimeOffset PreviousFrom,
    DateTimeOffset PreviousUntil,
    DateOnly PreviousAnchor,
    DateOnly NextAnchor,
    bool CanGoNext,
    string Granularity,
    int BucketSize,
    bool IsPartial
);

public sealed record AnalyticsMetrics(
    int Sessions,
    int CompletedSessions,
    decimal EnergyKWh,
    decimal CalculatedCost,
    int UnverifiedEnergySessions
);

public sealed record AnalyticsComparison(
    AnalyticsMetrics Previous,
    decimal? SessionsPercent,
    decimal? EnergyPercent,
    decimal? CostPercent
);

public sealed record AnalyticsTrendPoint(
    DateTimeOffset From,
    DateTimeOffset Until,
    int? Sessions,
    int? CompletedSessions,
    decimal? EnergyKWh,
    decimal? CalculatedCost
);

public sealed record AnalyticsRanking(
    Guid? Id,
    string Name,
    string? Code,
    int Sessions,
    int CompletedSessions,
    decimal EnergyKWh,
    decimal CalculatedCost
);

public sealed record AnalyticsOutcome(string Kind, int Sessions, decimal Percent);

public sealed record AnalyticsPeak(DateTimeOffset From, DateTimeOffset Until, int Sessions);

public sealed record OperationsCurrent(
    int Stations,
    IReadOnlyDictionary<string, int> StatusCounts,
    int Locations,
    int Administrators,
    int Drivers,
    int ActiveSessions,
    DateTimeOffset UpdatedAt
);

public sealed record OperationsAnalytics(
    AnalyticsPeriod Period,
    OperationsCurrent Current,
    AnalyticsMetrics Metrics,
    AnalyticsComparison Comparison,
    IReadOnlyList<AnalyticsTrendPoint> Trend,
    IReadOnlyList<AnalyticsRanking> StationRanking,
    IReadOnlyList<AnalyticsRanking> LocationRanking,
    IReadOnlyList<AnalyticsOutcome> Outcomes,
    AnalyticsPeak? PeakUsage,
    string Currency,
    string Attribution,
    DateTimeOffset UpdatedAt
);

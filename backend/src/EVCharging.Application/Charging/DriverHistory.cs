namespace EVCharging.Application.Charging;

public sealed record DriverHistoryFilter(
    string Period = "7d",
    DateOnly? From = null,
    DateOnly? Until = null,
    Guid? StationId = null,
    string Energy = "all",
    string? Currency = null,
    int Page = 1,
    int PageSize = 8,
    DateOnly? Bucket = null
);

public sealed record HistoryRange(DateOnly From, DateOnly Until);

public sealed record HistorySummary(
    int Charges,
    decimal EnergyKWh,
    IReadOnlyDictionary<string, decimal> Costs
);

public sealed record HistoryBucket(
    string Key,
    string Date,
    DateOnly From,
    DateOnly Until,
    int Charges,
    decimal EnergyKWh,
    IReadOnlyDictionary<string, decimal> Costs
);

public sealed record HistoryChart(string Granularity, IReadOnlyList<HistoryBucket> Buckets);

public sealed record HistoryStation(Guid StationId, string StationName, string StationCode);

public sealed record DriverHistoryPage(
    IReadOnlyList<DriverHistoryDto> Items,
    int Total,
    int Page,
    int PageSize,
    int Pages,
    int Offset,
    HistoryRange? Bounds,
    HistoryRange? Range,
    HistorySummary Summary,
    HistoryChart Chart,
    IReadOnlyList<HistoryStation> Stations,
    IReadOnlyList<string> Currencies,
    bool HasDemo
);

public interface IDriverHistoryQueries
{
    Task<DriverHistoryPage> SearchAsync(
        Guid driverId,
        DriverHistoryFilter filter,
        CancellationToken ct
    );
}

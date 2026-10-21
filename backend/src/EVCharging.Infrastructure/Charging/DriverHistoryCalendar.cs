using System.Linq.Expressions;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverHistoryCalendar
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Tirane"
    );
    public HistoryRange Range { get; }
    public string Granularity { get; }
    public List<HistoryRange> Buckets { get; } = [];

    public static DateOnly Day(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, Zone).DateTime);

    public static DateTimeOffset Midnight(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone));

    public static RequestFailure Invalid() =>
        new(400, "invalid_history_filter", "Choose a valid period, date range, filter and page.");

    public static void Validate(DriverHistoryFilter filter)
    {
        if (
            filter.Period is not ("7d" or "30d" or "6m" or "custom")
            || filter.Energy is not ("all" or "small" or "medium" or "large")
            || filter.Page < 1
            || filter.Page > 1_000_000
            || filter.PageSize < 1
            || filter.PageSize > 100
            || filter.Currency is { } currency
                && (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            || filter.Period == "custom"
                && (filter.From is null || filter.Until is null || filter.From > filter.Until)
            || filter.Period != "custom" && (filter.From is not null || filter.Until is not null)
            || filter.From?.Year < 1900
            || filter.Until?.Year > 9998
            || filter.Bucket?.Year < 1900
        )
            throw Invalid();
    }

    public DriverHistoryCalendar(DriverHistoryFilter filter, DateOnly latest)
    {
        Validate(filter);
        var until = filter.Period == "custom" ? filter.Until!.Value : latest;
        var from = filter.Period switch
        {
            "custom" => filter.From!.Value,
            "6m" => new DateOnly(until.Year, until.Month, 1).AddMonths(-5),
            "30d" => until.AddDays(-29),
            _ => until.AddDays(-6),
        };
        Range = new(from, until);
        var days = until.DayNumber - from.DayNumber + 1;
        Granularity =
            filter.Period == "6m" || days > 93 ? "month"
            : days > 31 ? "week"
            : "day";
        var months = (until.Year - from.Year) * 12 + until.Month - from.Month + 1;
        var step = Math.Max(1, (int)Math.Ceiling(months / 62m));
        while (from <= until)
        {
            var remainingMonths = (until.Year - from.Year) * 12 + until.Month - from.Month + 1;
            var next =
                Granularity == "month" && remainingMonths <= step
                    ? until.AddDays(1)
                    : Granularity switch
                    {
                        "month" => new DateOnly(from.Year, from.Month, 1).AddMonths(step),
                        "week" => from.AddDays(7),
                        _ => from.AddDays(1),
                    };
            Buckets.Add(new(from, next > until ? until : next.AddDays(-1)));
            from = next;
        }
    }

    // Bounded CASE projection: grouping and sums execute in PostgreSQL, not on a
    // downloaded list of sessions. Independent midnights preserve DST boundaries.
    public Expression<Func<ChargingHistory, HistoryAggregateRow>> Projection()
    {
        var row = Expression.Parameter(typeof(ChargingHistory), "row");
        Expression bucket = Expression.Constant(Buckets.Count - 1);
        for (var i = Buckets.Count - 2; i >= 0; i--)
            bucket = Expression.Condition(
                Expression.LessThan(
                    Expression.Property(row, nameof(ChargingHistory.EndedAt)),
                    Expression.Constant(Midnight(Buckets[i].Until.AddDays(1)))
                ),
                Expression.Constant(i),
                bucket
            );
        var fields = new[]
        {
            nameof(HistoryAggregateRow.Currency),
            nameof(HistoryAggregateRow.EnergyKWh),
            nameof(HistoryAggregateRow.TotalCost),
            nameof(HistoryAggregateRow.IsDemo),
        };
        var bindings = fields
            .Select(name =>
                Expression.Bind(
                    typeof(HistoryAggregateRow).GetProperty(name)!,
                    Expression.Property(row, name)
                )
            )
            .ToList();
        bindings.Add(
            Expression.Bind(
                typeof(HistoryAggregateRow).GetProperty(nameof(HistoryAggregateRow.Bucket))!,
                bucket
            )
        );
        return Expression.Lambda<Func<ChargingHistory, HistoryAggregateRow>>(
            Expression.MemberInit(Expression.New(typeof(HistoryAggregateRow)), bindings),
            row
        );
    }
}

public sealed class HistoryAggregateRow
{
    public int Bucket { get; set; }
    public string Currency { get; set; } = "";
    public decimal EnergyKWh { get; set; }
    public decimal TotalCost { get; set; }
    public bool IsDemo { get; set; }
}

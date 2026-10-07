using System.Linq.Expressions;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;

namespace EVCharging.Infrastructure.Charging;

// Calendar boundaries are converted independently, so DST days are not assumed to be 24 hours.
public sealed class AnalyticsCalendar
{
    public AnalyticsPeriod Period { get; }
    public IReadOnlyList<(DateTimeOffset From, DateTimeOffset Until)> Buckets { get; }

    public AnalyticsCalendar(AnalyticsFilter filter, DateTimeOffset now)
    {
        try
        {
            if (
                filter.Period is not ("Daily" or "Weekly" or "Monthly" or "Custom")
                || string.IsNullOrWhiteSpace(filter.TimeZone)
                || filter.TimeZone.Length > 100
                || filter.Period != "Custom"
                    && (filter.From is not null || filter.Until is not null)
                || filter.Period == "Custom" && filter.Date is not null
            )
                throw Invalid();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(filter.TimeZone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var anchor = filter.Date ?? today;
            var start = filter.Period switch
            {
                "Daily" => anchor,
                "Weekly" => anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7),
                "Monthly" => new DateOnly(anchor.Year, anchor.Month, 1),
                _ => filter.From ?? throw Invalid(),
            };
            var end = filter.Period switch
            {
                "Daily" => start,
                "Weekly" => start.AddDays(6),
                "Monthly" => start.AddMonths(1).AddDays(-1),
                _ => filter.Until ?? throw Invalid(),
            };
            if (start > today || end < start || filter.Period == "Custom" && end > today)
                throw Invalid();
            var days = end.DayNumber - start.DayNumber + 1;
            var previous = filter.Period == "Monthly" ? start.AddMonths(-1) : start.AddDays(-days);
            var next = filter.Period == "Monthly" ? start.AddMonths(1) : end.AddDays(1);
            var granularity =
                filter.Period == "Daily" || filter.Period == "Custom" && days <= 2 ? "Hourly"
                : filter.Period is "Weekly" or "Monthly" || days <= 62 ? "Daily"
                : days <= 434 ? "Weekly"
                : "Monthly";
            var months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
            var size = granularity == "Monthly" ? Math.Max(1, (int)Math.Ceiling(months / 62m)) : 1;
            DateTimeOffset Midnight(DateOnly date) =>
                new(
                    TimeZoneInfo.ConvertTimeToUtc(
                        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                        zone
                    )
                );
            var from = Midnight(start);
            var until = Midnight(end.AddDays(1));
            Period = new(
                filter.Period,
                zone.Id,
                today,
                start,
                end,
                from,
                until,
                now < until ? now : until,
                Midnight(previous),
                from,
                previous,
                next,
                next <= today,
                granularity,
                size,
                now < until
            );
            var buckets = new List<(DateTimeOffset From, DateTimeOffset Until)>();
            if (granularity == "Hourly")
            {
                for (var cursor = from; cursor < until; cursor = cursor.AddHours(1))
                    buckets.Add((cursor, cursor.AddHours(1) < until ? cursor.AddHours(1) : until));
            }
            else
            {
                var cursor = start;
                while (cursor <= end)
                {
                    var boundary = granularity switch
                    {
                        "Daily" => cursor.AddDays(1),
                        // Align subsequent weekly/monthly buckets with the calendar; first/last may be partial.
                        "Weekly" => cursor.AddDays(7 - ((int)cursor.DayOfWeek + 6) % 7),
                        _ => new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(size),
                    };
                    if (boundary > end.AddDays(1))
                        boundary = end.AddDays(1);
                    buckets.Add((Midnight(cursor), Midnight(boundary)));
                    cursor = boundary;
                }
            }
            Buckets = buckets;
        }
        catch (Exception error)
            when (error
                    is ArgumentException
                        or TimeZoneNotFoundException
                        or InvalidTimeZoneException
            )
        {
            throw Invalid();
        }
    }

    // EF translates this to a bounded CASE expression and groups in PostgreSQL.
    public Expression<Func<ChargingSession, int>> BucketKey(bool completion)
    {
        var session = Expression.Parameter(typeof(ChargingSession), "session");
        Expression time = Expression.Property(session, completion ? "EndedAt" : "StartedAt");
        if (completion)
            time = Expression.Property(time, "Value");
        Expression index = Expression.Constant(Buckets.Count - 1);
        for (var i = Buckets.Count - 2; i >= 0; i--)
            index = Expression.Condition(
                Expression.LessThan(time, Expression.Constant(Buckets[i].Until)),
                Expression.Constant(i),
                index
            );
        return Expression.Lambda<Func<ChargingSession, int>>(index, session);
    }

    private static RequestFailure Invalid() =>
        new(
            400,
            "invalid_analytics_filter",
            "Choose Daily, Weekly, Monthly or Custom with valid calendar dates and a time zone. Future periods are unavailable."
        );
}

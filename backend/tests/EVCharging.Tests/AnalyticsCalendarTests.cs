using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Tests;

public sealed class AnalyticsCalendarTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");

    private static DateOnly Day(string value) => DateOnly.Parse(value);

    [Fact]
    public void DefaultsUseNetworkCalendarAndClipOnlyDataNotCalendarBuckets()
    {
        var daily = new AnalyticsCalendar(new(), DateTimeOffset.Parse("2026-10-06T22:30:00Z"));
        Assert.Equal(Day("2026-10-07"), daily.Period.Today);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T22:00:00Z"), daily.Period.From);
        Assert.Equal(24, daily.Buckets.Count);
        Assert.False(daily.Period.CanGoNext);
        Assert.True(daily.Period.IsPartial);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T22:30:00Z"), daily.Period.EffectiveUntil);
        var week = new AnalyticsCalendar(new("Weekly"), Now);
        Assert.Equal(Day("2026-10-05"), week.Period.StartDate);
        Assert.Equal(Day("2026-10-11"), week.Period.EndDate);
        Assert.Equal(Day("2026-09-28"), week.Period.PreviousAnchor);
        Assert.Equal(7, week.Buckets.Count);
        Assert.Equal("Daily", week.Period.Granularity);
        var month = new AnalyticsCalendar(new("Monthly"), Now);
        Assert.Equal(Day("2026-10-01"), month.Period.StartDate);
        Assert.Equal(Day("2026-10-31"), month.Period.EndDate);
        Assert.Equal(Day("2026-09-01"), month.Period.PreviousAnchor);
        Assert.Equal(31, month.Buckets.Count);
        Assert.Equal("Daily", month.Period.Granularity);
    }

    [Theory]
    [InlineData("2026-03-29", 23)]
    [InlineData("2026-10-25", 25)]
    [InlineData("2026-10-06", 24)]
    public void DailyHourlyBucketsRespectDstAndRemainContiguous(string day, int hours)
    {
        var calendar = new AnalyticsCalendar(
            new(Date: Day(day)),
            DateTimeOffset.Parse("2026-12-01T00:00:00Z")
        );
        Assert.Equal(hours, calendar.Buckets.Count);
        Assert.Equal(hours, (calendar.Period.Until - calendar.Period.From).TotalHours);
        Assert.Equal(calendar.Period.From, calendar.Buckets[0].From);
        Assert.Equal(calendar.Period.Until, calendar.Buckets[^1].Until);
        var key = calendar.BucketKey(false).Compile();
        for (var i = 0; i < hours; i++)
        {
            Assert.Equal(i, key(new ChargingSession { StartedAt = calendar.Buckets[i].From }));
            Assert.Equal(
                i,
                key(new ChargingSession { StartedAt = calendar.Buckets[i].Until.AddTicks(-1) })
            );
            if (i > 0)
                Assert.Equal(calendar.Buckets[i - 1].Until, calendar.Buckets[i].From);
        }
    }

    [Fact]
    public void MonthComparisonUsesPreviousCalendarMonthAndLeapFebruaryKeepsDailyPoints()
    {
        var march = new AnalyticsCalendar(new("Monthly", Day("2024-03-17")), Now);
        Assert.Equal(Day("2024-02-01"), march.Period.PreviousAnchor);
        Assert.Equal(29, (march.Period.PreviousUntil - march.Period.PreviousFrom).TotalDays);
        var february = new AnalyticsCalendar(new("Monthly", Day("2024-02-02")), Now);
        Assert.Equal(29, february.Buckets.Count);
        Assert.True(february.Period.CanGoNext);
        Assert.False(february.Period.IsPartial);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-06", "Hourly")]
    [InlineData("2026-09-01", "2026-10-06", "Daily")]
    [InlineData("2026-01-01", "2026-10-06", "Weekly")]
    [InlineData("2010-01-01", "2026-10-06", "Monthly")]
    [InlineData("1900-01-01", "2026-10-06", "Monthly")]
    public void CustomRangeIsInclusiveAndAutomaticallyBounded(
        string from,
        string until,
        string granularity
    )
    {
        var calendar = new AnalyticsCalendar(
            new("Custom", From: Day(from), Until: Day(until)),
            Now
        );
        Assert.Equal(granularity, calendar.Period.Granularity);
        Assert.InRange(calendar.Buckets.Count, 1, 64);
        var days = Day(until).DayNumber - Day(from).DayNumber + 1;
        Assert.Equal(Day(from).AddDays(-days), calendar.Period.PreviousAnchor);
        Assert.Equal(calendar.Period.From, calendar.Period.PreviousUntil);
        Assert.Equal(
            Day(until).AddDays(1),
            DateOnly.FromDateTime(
                TimeZoneInfo
                    .ConvertTime(
                        calendar.Period.Until,
                        TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane")
                    )
                    .DateTime
            )
        );
    }

    [Theory]
    [InlineData("Daily", "2026-10-08", null, null, "Europe/Tirane")]
    [InlineData("Weekly", "2026-10-12", null, null, "Europe/Tirane")]
    [InlineData("Monthly", "2026-11-01", null, null, "Europe/Tirane")]
    [InlineData("Custom", null, "2026-10-06", "2026-10-08", "Europe/Tirane")]
    [InlineData("Custom", null, "2026-10-06", "2026-10-05", "Europe/Tirane")]
    [InlineData("Custom", null, null, "2026-10-06", "Europe/Tirane")]
    [InlineData("Custom", "2026-10-01", "2026-10-01", "2026-10-02", "Europe/Tirane")]
    [InlineData("Daily", null, "2026-10-01", null, "Europe/Tirane")]
    [InlineData("Yearly", null, null, null, "Europe/Tirane")]
    [InlineData("Daily", null, null, null, "Invalid/Zone")]
    [InlineData("Daily", "0001-01-01", null, null, "Europe/Tirane")]
    public void InvalidAndFuturePeriodsAreRejected(
        string period,
        string? date,
        string? from,
        string? until,
        string zone
    )
    {
        var error = Assert.Throws<RequestFailure>(() =>
            new AnalyticsCalendar(
                new(
                    period,
                    date is null ? null : Day(date),
                    from is null ? null : Day(from),
                    until is null ? null : Day(until),
                    zone
                ),
                Now
            )
        );
        Assert.Equal("invalid_analytics_filter", error.Code);
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("Monthly")]
    [InlineData("Custom")]
    public void BucketsTranslateToPostgresGroupingWithoutFetchingSessions(string period)
    {
        using var db = new ChargingDbContext(
            new DbContextOptionsBuilder<ChargingDbContext>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused")
                .Options
        );
        var calendar = new AnalyticsCalendar(
            period == "Custom"
                ? new("Custom", From: Day("2020-01-01"), Until: Day("2026-10-06"))
                : new(period),
            Now
        );
        var sql = db
            .ChargingSessions.Where(x =>
                x.EndedAt >= calendar.Period.From && x.EndedAt < calendar.Period.EffectiveUntil
            )
            .GroupBy(calendar.BucketKey(true))
            .Select(g => new
            {
                Index = g.Key,
                Count = g.Count(),
                Energy = g.Sum(x => x.EnergyKWh ?? 0),
                Cost = g.Sum(x =>
                    decimal.Floor((x.EnergyKWh ?? 0) * x.UnitPriceSnapshot * 100 + 0.5m) / 100
                ),
            })
            .ToQueryString();
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("floor", sql);
    }
}

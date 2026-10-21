using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class DriverHistorySearchTests
{
    [Fact]
    public void LongCustomRangesHaveBoundedBucketsWithoutDateOverflow()
    {
        var calendar = new DriverHistoryCalendar(
            new("custom", new(1900, 1, 1), new(9998, 12, 31)),
            new(2026, 10, 21)
        );
        Assert.InRange(calendar.Buckets.Count, 1, 62);
        Assert.Equal(new DateOnly(9998, 12, 31), calendar.Buckets[^1].Until);
    }

    private static async Task<Guid> Register(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                email,
                "long-driver-passphrase",
                "Test",
                "Driver",
                "en",
                "+355691234567"
            )
        );
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        return auth.User.Id;
    }

    private static ChargingHistory Entry(
        Guid driver,
        ChargingStation station,
        string end,
        decimal energy = 10,
        string currency = "EUR"
    ) =>
        new()
        {
            DriverId = driver,
            Station = station,
            StationId = station.Id,
            StartedAt = DateTimeOffset.Parse(end).AddHours(-1),
            EndedAt = DateTimeOffset.Parse(end),
            EnergyKWh = energy,
            TotalCost = energy * 0.3m,
            UnitPriceSnapshot = 0.3m,
            Currency = currency,
        };

    private static async Task<DriverHistoryPage> Search(HttpClient client, string query = "") =>
        (
            await client.GetFromJsonAsync<DriverHistoryPage>(
                "/api/v1/driver/history/search" + query
            )
        )!;

    [Fact]
    public async Task FullHistoryIsFilteredAndSummedBeforePagingWithPrivateStableResults()
    {
        await using var app = await AuthRateLimitTests.Start();
        using var client = app.GetTestClient();
        var driver = await Register(client, "history@example.com");
        var old = new ChargingStation
        {
            Name = "Old archived station",
            Code = "OLD",
            IsArchived = true,
        };
        var recent = new ChargingStation { Name = "Recent", Code = "RECENT" };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            db.Stations.AddRange(old, recent);
            for (var i = 0; i < 105; i++)
                db.ChargingHistory.Add(Entry(driver, recent, "2026-10-20T12:00:00Z"));
            db.ChargingHistory.AddRange(
                Entry(driver, old, "2020-01-01T12:00:00Z", 40),
                Entry(driver, recent, "2026-10-19T12:00:00Z", 20, "USD")
            );
            db.ChargingHistory.Add(Entry(Guid.NewGuid(), recent, "2026-10-20T12:00:00Z", 999));
            var demo = Entry(driver, recent, "2026-10-20T12:00:00Z", 999);
            demo.IsDemo = true;
            db.ChargingHistory.Add(demo);
            var demoStation = new ChargingStation
            {
                Name = "Hidden demo",
                Code = "DEMO",
                IsDemo = true,
            };
            db.Stations.Add(demoStation);
            db.ChargingHistory.Add(Entry(driver, demoStation, "2026-10-20T12:00:00Z", 999));
            await db.SaveChangesAsync();
        }
        const string query = "?period=custom&from=2020-01-01&until=2026-10-21";
        var first = await Search(client, query);
        Assert.Equal(107, first.Total);
        Assert.Equal(107, first.Summary.Charges);
        Assert.Equal(1110m, first.Summary.EnergyKWh);
        Assert.Equal(327m, first.Summary.Costs["EUR"]);
        Assert.Equal(6m, first.Summary.Costs["USD"]);
        Assert.Equal(8, first.Items.Count);
        Assert.Equal(14, first.Pages);
        Assert.Equal(107, first.Chart.Buckets.Sum(x => x.Charges));
        Assert.True(first.Chart.Buckets.Count <= 62);
        Assert.Equal(2, first.Stations.Count);
        Assert.False(first.HasDemo);
        var ids = new List<Guid>();
        for (var page = 1; page <= first.Pages; page++)
        {
            var result = await Search(client, query + "&page=" + page);
            Assert.Equal(first.Total, result.Total);
            ids.AddRange(result.Items.Select(x => x.Id));
            Assert.Equal((page - 1) * 8, result.Offset);
        }
        Assert.Equal(107, ids.Distinct().Count());
        Assert.Equal(
            first.Items.Select(x => x.Id),
            (await Search(client, query)).Items.Select(x => x.Id)
        );
        var oldest = await Search(client, "?period=custom&from=2020-01-01&until=2020-01-01");
        Assert.Single(oldest.Items);
        Assert.Equal(old.Id, oldest.Items[0].StationId);
        var large = await Search(client, query + "&energy=large&stationId=" + old.Id);
        Assert.Single(large.Items);
        Assert.Equal(40m, large.Summary.EnergyKWh);
        var usd = await Search(client, query + "&currency=USD&page=999");
        Assert.Equal(1, usd.Page);
        Assert.Single(usd.Items);
        Assert.Single(usd.Summary.Costs);
        using var other = app.GetTestClient();
        await Register(other, "empty@example.com");
        var empty = await Search(other, query);
        Assert.Empty(empty.Items);
        Assert.Empty(empty.Stations);
        Assert.Empty(empty.Currencies);
        Assert.Null(empty.Bounds);
        using var anonymous = app.GetTestClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/driver/history/search")).StatusCode
        );
    }

    [Fact]
    public async Task CompletionDatesAndBucketSelectionRespectTiranaDst()
    {
        await using var app = await AuthRateLimitTests.Start();
        using var client = app.GetTestClient();
        var driver = await Register(client, "dst@example.com");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var station = new ChargingStation { Name = "DST", Code = "DST" };
            db.Stations.Add(station);
            db.ChargingHistory.AddRange(
                Entry(driver, station, "2026-10-24T21:59:59Z"),
                Entry(driver, station, "2026-10-24T22:30:00Z"),
                Entry(driver, station, "2026-10-25T22:30:00Z"),
                Entry(driver, station, "2026-10-25T23:00:00Z")
            );
            await db.SaveChangesAsync();
        }
        var day = await Search(client, "?period=custom&from=2026-10-25&until=2026-10-25");
        Assert.Equal(2, day.Total);
        Assert.Single(day.Chart.Buckets);
        Assert.Equal(20m, day.Summary.EnergyKWh);
        var selected = await Search(client, "?period=7d&bucket=2026-10-25");
        Assert.Equal(2, selected.Total);
        Assert.Equal(4, selected.Chart.Buckets.Sum(x => x.Charges));
        var emptyDay = await Search(client, "?period=7d&bucket=2026-10-21");
        Assert.Empty(emptyDay.Items);
        Assert.Equal(0, emptyDay.Total);
        Assert.Equal(new DateOnly(2026, 5, 1), (await Search(client, "?period=6m")).Range!.From);
    }

    [Theory]
    [InlineData("period=custom&from=2026-10-02&until=2026-10-01")]
    [InlineData("period=custom&from=2026-02-30&until=2026-03-01")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("period=all")]
    [InlineData("energy=invalid")]
    [InlineData("currency=EUR%27")]
    [InlineData("stationId=invalid")]
    [InlineData("from=2026-01-01")]
    public async Task InvalidQueriesReturnBadRequest(string query)
    {
        await using var app = await AuthRateLimitTests.Start();
        using var client = app.GetTestClient();
        await Register(client, "invalid@example.com");
        using var response = await client.GetAsync("/api/v1/driver/history/search?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void PostgresTranslatesBucketGroupingAndPaginationWithoutFetchingRows()
    {
        using var db = new ChargingDbContext(
            new DbContextOptionsBuilder<ChargingDbContext>()
                .UseNpgsql("Host=localhost;Database=translation_only")
                .Options
        );
        var calendar = new DriverHistoryCalendar(
            new("custom", new(2026, 10, 24), new(2026, 10, 26)),
            new(2026, 10, 26)
        );
        var sql = db
            .ChargingHistory.Where(x => x.DriverId == Guid.Empty)
            .Select(calendar.Projection())
            .GroupBy(x => new { x.Bucket, x.Currency })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.Currency,
                Count = g.Count(),
                Energy = g.Sum(x => x.EnergyKWh),
                Cost = g.Sum(x => x.TotalCost),
            })
            .ToQueryString();
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("sum(", sql);
        var page = db
            .ChargingHistory.OrderByDescending(x => x.EndedAt)
            .ThenByDescending(x => x.Id)
            .Skip(104)
            .Take(8)
            .ToQueryString();
        Assert.Contains("LIMIT", page);
        Assert.Contains("OFFSET", page);
    }
}

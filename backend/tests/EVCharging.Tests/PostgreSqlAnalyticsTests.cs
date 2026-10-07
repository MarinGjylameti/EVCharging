using EVCharging.Application.Charging;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class PostgreSqlChargingTests
{
    [PostgreSqlFact]
    public async Task AnalyticsAggregatesRankingsAndCalendarBucketsExecuteInPostgres()
    {
        await using var test = await DatabaseTest.CreateAsync();
        using var scope = test.Scope();
        var db =
            scope.ServiceProvider.GetRequiredService<EVCharging.Infrastructure.Persistence.ChargingDbContext>();
        var seed = await AnalyticsTestData.CreateAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
        // Historical full days are independent of the wall clock in CI.
        var result = await service.AnalyticsAsync(
            seed.AdminId,
            new(Date: new(2026, 10, 6)),
            default
        );
        Assert.Equal(new AnalyticsMetrics(2, 1, 2m, .70m, 0), result.Metrics);
        Assert.Equal(24, result.Trend.Count);
        Assert.Equal(2m, Assert.Single(result.StationRanking).EnergyKWh);
        Assert.Equal(2m, Assert.Single(result.LocationRanking).EnergyKWh);
        Assert.Equal(1, Assert.Single(result.Outcomes).Sessions);
        var month = await service.AnalyticsAsync(
            seed.AdminId,
            new("Monthly", new(2026, 9, 1)),
            default
        );
        Assert.Equal(30, month.Trend.Count);
        var custom = await service.AnalyticsAsync(
            seed.AdminId,
            new("Custom", From: new(2020, 1, 1), Until: new(2026, 10, 6)),
            default
        );
        Assert.Equal("Monthly", custom.Period.Granularity);
        Assert.Equal(2m, custom.Metrics.EnergyKWh);
    }
}

using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class PostgreSqlChargingTests
{
    [PostgreSqlFact]
    public async Task DriverHistoryAggregatesAndPagesInOnePostgresSnapshot()
    {
        await using var test = await DatabaseTest.CreateAsync();
        var (driver, other) = await test.DriversAsync();
        await using (var db = test.Context())
        {
            var station = new ChargingStation
            {
                Code = "HISTORY",
                Name = "History",
                City = "Tirana",
                Address = "Test",
                ConnectorType = "Type 2",
                ConnectorCount = 1,
                PowerKW = 22,
                PricePerKWh = .3m,
            };
            db.Stations.Add(station);
            for (var i = 0; i < 107; i++)
                db.ChargingHistory.Add(
                    new ChargingHistory
                    {
                        DriverId = i == 106 ? other : driver,
                        Station = station,
                        StationId = station.Id,
                        StartedAt = DateTimeOffset.Parse("2026-10-24T21:30:00Z"),
                        EndedAt = DateTimeOffset.Parse("2026-10-25T22:30:00Z"),
                        EnergyKWh = 10,
                        UnitPriceSnapshot = .3m,
                        TotalCost = 3,
                        Currency = i == 105 ? "USD" : "EUR",
                    }
                );
            await db.SaveChangesAsync();
        }
        using var scope = test.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IDriverHistoryQueries>();
        var result = await service.SearchAsync(
            driver,
            new("custom", new(2026, 10, 25), new(2026, 10, 25), Page: 14),
            default
        );
        Assert.Equal(106, result.Total);
        Assert.Equal(1060m, result.Summary.EnergyKWh);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(315m, result.Summary.Costs["EUR"]);
        Assert.Equal(3m, result.Summary.Costs["USD"]);
        Assert.Equal(106, Assert.Single(result.Chart.Buckets).Charges);
        Assert.Single(result.Stations);
    }
}

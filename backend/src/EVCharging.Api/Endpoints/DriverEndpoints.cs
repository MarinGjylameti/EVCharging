using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api.Endpoints;

public static class DriverEndpoints
{
    public static void MapDriverEndpoints(this WebApplication app)
    {
        var driver = app.MapGroup("/api/v1/driver").RequireAuthorization("Driver");
        driver.MapGet("/stations", async (ChargingDbContext db, CancellationToken ct) =>
        {
            var stations = await db.Stations.AsNoTracking().Where(x => !x.IsArchived)
                .OrderBy(x => x.City).ThenBy(x => x.Name).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            return Results.Ok(stations.Select(x => new
            {
                x.Id, x.Code, x.Name, x.City, x.Address, x.Latitude, x.Longitude,
                x.ConnectorType, x.ConnectorCount, x.PowerKW, x.PricePerKWh,
                x.Currency, status = x.Status.ToString(), x.IsDemo, x.Version,
                availabilityForecast = AvailabilityForecastRules.ForDriver(x, now)
            }));
        });
        driver.MapGet("/history", async (ClaimsPrincipal user, ChargingDbContext db, CancellationToken ct) =>
        {
            var driverId = user.UserId();
            return Results.Ok(await db.ChargingHistory.AsNoTracking()
                .Where(x => x.DriverId == driverId)
                .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                .Take(100)
                .Select(x => new
                {
                    x.Id, x.StartedAt, x.EndedAt, x.EnergyKWh, x.UnitPriceSnapshot,
                    x.TotalCost, x.Currency, x.IsDemo,
                    stationId = x.StationId, stationCode = x.Station.Code,
                    stationName = x.Station.Name, city = x.Station.City
                }).ToListAsync(ct));
        });
    }
}

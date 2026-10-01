using System.Net;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Persistence;
using EVCharging.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.BrowserTestHost;

// Disposable fixture host only; never referenced or published by the product API.
internal static class BrowserTestEntry
{
    public static async Task Main(string[] args)
    {
        if (!args.Contains("--local-browser-test"))
            throw new InvalidOperationException("Pass --local-browser-test to run this disposable fixture host.");
        await using var factory = new ApiFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 5202));
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var hasher = new PasswordHasher<Account>();

        var operatorAccount = new Account
        {
            Email = "operator@example.test", NormalizedEmail = "OPERATOR@EXAMPLE.TEST",
            FirstName = "Operator", LastName = "Test", Role = UserRole.Superadmin
        };
        operatorAccount.PasswordHash = hasher.HashPassword(operatorAccount, "Local-test-only-2026!");
        var driver = new Account
        {
            Email = "driver@example.test", NormalizedEmail = "DRIVER@EXAMPLE.TEST",
            FirstName = "Arta", LastName = "Deda", Role = UserRole.Driver,
            VehicleMake = "Volkswagen", VehicleModel = "ID.4", BatteryType = "77 kWh"
        };
        driver.PasswordHash = hasher.HashPassword(driver, "Local-driver-demo-2026!");
        db.Accounts.AddRange(operatorAccount, driver);
        db.Notifications.Add(new UserNotification
        {
            UserId = operatorAccount.Id, SourceEventId = "fixture-only",
            Kind = NotificationKind.ChargingCompleted
        });

        // Coordinates and addresses are illustrative demo positions, not surveyed station locations.
        var now = DateTimeOffset.UtcNow;
        var stations = new[]
        {
            Station("EV-TIR-001", "Sheshi Skënderbej", "Tiranë", "Qendra e qytetit", 41.3275, 19.8187, "CCS2", 2, 150, 0.35m, PointState.Available),
            Station("EV-TIR-002", "Blloku", "Tiranë", "Rruga Ibrahim Rugova", 41.3211, 19.8174, "CCS2", 2, 120, 0.35m, PointState.Charging),
            Station("EV-TIR-003", "Pazari i Ri", "Tiranë", "Rruga Qemal Stafa", 41.3296, 19.8256, "Type 2", 4, 22, 0.30m, PointState.Available),
            Station("EV-TIR-004", "Parku i Liqenit", "Tiranë", "Rruga Sami Frashëri", 41.3134, 19.8133, "CCS2", 2, 75, 0.32m, PointState.Occupied),
            Station("EV-TIR-005", "Rruga e Kavajës", "Tiranë", "Rruga e Kavajës", 41.3244, 19.8089, "CCS2", 2, 60, 0.34m, PointState.Maintenance),
            Station("EV-TIR-006", "Stadiumi", "Tiranë", "Rruga Dervish Hima", 41.3181, 19.8241, "CCS2", 2, 180, 0.38m, PointState.Available),
            Station("EV-DUR-001", "Porti i Durrësit", "Durrës", "Lagjja 1, pranë portit", 41.3136, 19.4556, "CCS2", 2, 120, 0.35m, PointState.Available),
            Station("EV-DUR-002", "Vollga", "Durrës", "Shëtitorja Taulantia", 41.3128, 19.4487, "CCS2", 2, 75, 0.36m, PointState.Charging),
            Station("EV-DUR-003", "Stadiumi Niko Dovana", "Durrës", "Rruga e Stadiumit", 41.3185, 19.4593, "Type 2", 2, 22, 0.29m, PointState.OutOfService),
            Station("EV-LUS-001", "Qendra e Lushnjës", "Lushnjë", "Rruga Kongresi i Lushnjës", 40.9419, 19.7050, "CCS2", 2, 100, 0.35m, PointState.Charging)
        };
        stations[^1].ForecastConnectorId = 1;
        stations[^1].ForecastAvailableAt = now.AddMinutes(40);
        stations[^1].ForecastObservedAt = now;
        stations[^1].ForecastSource = "Demo";
        db.Stations.AddRange(stations);
        var history = new (int Station, int DaysAgo, decimal KWh, int Minutes)[]
        {
            (0, 1, 26.4m, 48), (6, 3, 18.7m, 37), (2, 6, 12.8m, 72),
            (5, 10, 31.6m, 34), (1, 15, 21.2m, 43), (7, 22, 16.5m, 39)
        };
        foreach (var item in history)
        {
            var station = stations[item.Station];
            var end = new DateTimeOffset(
                now.AddDays(-item.DaysAgo).UtcDateTime.Date.AddHours(18).AddMinutes(item.Minutes),
                TimeSpan.Zero);
            db.ChargingHistory.Add(new ChargingHistory
            {
                DriverId = driver.Id, StationId = station.Id, Station = station,
                StartedAt = end.AddMinutes(-item.Minutes), EndedAt = end,
                EnergyKWh = item.KWh, UnitPriceSnapshot = station.PricePerKWh,
                TotalCost = decimal.Round(item.KWh * station.PricePerKWh, 2, MidpointRounding.AwayFromZero),
                Currency = "EUR", IsDemo = true
            });
        }
        await db.SaveChangesAsync();
        Console.WriteLine("BROWSER TEST ONLY: http://127.0.0.1:5202 — 10 demo stations, 6 demo sessions, no external push.");
        Console.WriteLine("Driver: driver@example.test / Local-driver-demo-2026!");
        Console.WriteLine("Operator: operator@example.test / Local-test-only-2026!");
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        await stop.Task;
    }

    private static ChargingStation Station(string code, string name, string city, string address,
        double latitude, double longitude, string connector, int count, decimal power,
        decimal price, PointState status) => new()
        {
            Code = code, Name = name, City = city, Address = address,
            Latitude = latitude, Longitude = longitude,
            ConnectorType = connector, ConnectorCount = count, PowerKW = power,
            PricePerKWh = price, Currency = "EUR", Status = status, IsDemo = true
        };
}

using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api;

// Explicit, idempotent demo setup. Never runs during a normal API start.
public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["Demo:DriverEmail"]?.Trim();
        var password = configuration["Demo:DriverPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Demo seed requires Demo:DriverEmail and Demo:DriverPassword."
            );
        AccountRules.ValidatePassword(password);
        var normalizedEmail = AccountRules.NormalizeEmail(email);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        var driver = await db.Accounts.SingleOrDefaultAsync(x =>
            x.NormalizedEmail == normalizedEmail
        );
        if (driver is null)
        {
            driver = new Account
            {
                Email = email,
                NormalizedEmail = normalizedEmail,
                FirstName = "Arta",
                LastName = "Deda",
                Role = UserRole.Driver,
                VehicleMake = "Volkswagen",
                VehicleModel = "ID.4",
                BatteryType = "77 kWh",
            };
            driver.PasswordHash = hasher.HashPassword(driver, password);
            db.Accounts.Add(driver);
        }
        else if (driver.Role != UserRole.Driver)
            throw new InvalidOperationException(
                "The demo email already belongs to a non-Driver account."
            );

        var stationDefinitions = new[]
        {
            Station(
                "EV-TIR-001",
                "Sheshi Skënderbej",
                "Tiranë",
                "Qendra e qytetit",
                41.3275,
                19.8187,
                "CCS2",
                2,
                150,
                0.35m,
                PointState.Available
            ),
            Station(
                "EV-TIR-002",
                "Blloku",
                "Tiranë",
                "Rruga Ibrahim Rugova",
                41.3211,
                19.8174,
                "CCS2",
                2,
                120,
                0.35m,
                PointState.Charging
            ),
            Station(
                "EV-TIR-003",
                "Pazari i Ri",
                "Tiranë",
                "Rruga Qemal Stafa",
                41.3296,
                19.8256,
                "Type 2",
                4,
                22,
                0.30m,
                PointState.Available
            ),
            Station(
                "EV-TIR-004",
                "Parku i Liqenit",
                "Tiranë",
                "Rruga Sami Frashëri",
                41.3134,
                19.8133,
                "CCS2",
                2,
                75,
                0.32m,
                PointState.Occupied
            ),
            Station(
                "EV-TIR-005",
                "Rruga e Kavajës",
                "Tiranë",
                "Rruga e Kavajës",
                41.3244,
                19.8089,
                "CCS2",
                2,
                60,
                0.34m,
                PointState.Maintenance
            ),
            Station(
                "EV-TIR-006",
                "Stadiumi",
                "Tiranë",
                "Rruga Dervish Hima",
                41.3181,
                19.8241,
                "CCS2",
                2,
                180,
                0.38m,
                PointState.Available
            ),
            Station(
                "EV-DUR-001",
                "Porti i Durrësit",
                "Durrës",
                "Lagjja 1, pranë portit",
                41.3136,
                19.4556,
                "CCS2",
                2,
                120,
                0.35m,
                PointState.Available
            ),
            Station(
                "EV-DUR-002",
                "Vollga",
                "Durrës",
                "Shëtitorja Taulantia",
                41.3128,
                19.4487,
                "CCS2",
                2,
                75,
                0.36m,
                PointState.Charging
            ),
            Station(
                "EV-DUR-003",
                "Stadiumi Niko Dovana",
                "Durrës",
                "Rruga e Stadiumit",
                41.3185,
                19.4593,
                "Type 2",
                2,
                22,
                0.29m,
                PointState.OutOfService
            ),
            Station(
                "EV-LUS-001",
                "Qendra e Lushnjës",
                "Lushnjë",
                "Rruga Kongresi i Lushnjës",
                40.9419,
                19.7050,
                "CCS2",
                2,
                100,
                0.35m,
                PointState.Charging
            ),
        };
        var existing = await db.Stations.Where(x => x.IsDemo).ToDictionaryAsync(x => x.Code);
        foreach (var station in stationDefinitions)
        {
            if (existing.TryGetValue(station.Code, out var saved))
                continue;
            if (station.Code == "EV-LUS-001")
            {
                var now = DateTimeOffset.UtcNow;
                station.ForecastConnectorId = 1;
                station.ForecastAvailableAt = now.AddMinutes(40);
                station.ForecastObservedAt = now;
                station.ForecastSource = "Demo";
            }
            db.Stations.Add(station);
            existing.Add(station.Code, station);
        }

        if (!await db.ChargingHistory.AnyAsync(x => x.DriverId == driver.Id && x.IsDemo))
        {
            var now = DateTimeOffset.UtcNow;
            var history = new (string Code, int DaysAgo, decimal KWh, int Minutes)[]
            {
                ("EV-TIR-001", 1, 26.4m, 48),
                ("EV-DUR-001", 3, 18.7m, 37),
                ("EV-TIR-003", 6, 12.8m, 72),
                ("EV-TIR-006", 10, 31.6m, 34),
                ("EV-TIR-002", 15, 21.2m, 43),
                ("EV-DUR-002", 22, 16.5m, 39),
            };
            foreach (var item in history)
            {
                var station = existing[item.Code];
                var end = new DateTimeOffset(
                    now.AddDays(-item.DaysAgo)
                        .UtcDateTime.Date.AddHours(18)
                        .AddMinutes(item.Minutes),
                    TimeSpan.Zero
                );
                db.ChargingHistory.Add(
                    new ChargingHistory
                    {
                        DriverId = driver.Id,
                        StationId = station.Id,
                        Station = station,
                        StartedAt = end.AddMinutes(-item.Minutes),
                        EndedAt = end,
                        EnergyKWh = item.KWh,
                        UnitPriceSnapshot = station.PricePerKWh,
                        TotalCost = decimal.Round(
                            item.KWh * station.PricePerKWh,
                            2,
                            MidpointRounding.AwayFromZero
                        ),
                        Currency = "EUR",
                        IsDemo = true,
                    }
                );
            }
        }

        var adminEmail = configuration["Bootstrap:Email"]?.Trim();
        var adminPassword = configuration["Bootstrap:Password"];
        if (
            !string.IsNullOrWhiteSpace(adminEmail)
            && !string.IsNullOrWhiteSpace(adminPassword)
            && !await db.Accounts.AnyAsync(x => x.Role == UserRole.Superadmin)
        )
        {
            AccountRules.ValidatePassword(adminPassword);
            var normalizedAdminEmail = AccountRules.NormalizeEmail(adminEmail);
            if (await db.Accounts.AnyAsync(x => x.NormalizedEmail == normalizedAdminEmail))
                throw new InvalidOperationException(
                    "The admin email already belongs to another account."
                );
            var admin = new Account
            {
                Email = adminEmail,
                NormalizedEmail = normalizedAdminEmail,
                FirstName = "System",
                LastName = "Administrator",
                Role = UserRole.Superadmin,
            };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.Accounts.Add(admin);
            db.Audit.Add(
                new AuditEntry
                {
                    ActorId = admin.Id,
                    ActorRole = "Superadmin",
                    EntityId = admin.Id,
                    Action = "SuperadminBootstrapped",
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static ChargingStation Station(
        string code,
        string name,
        string city,
        string address,
        double latitude,
        double longitude,
        string connector,
        int count,
        decimal power,
        decimal price,
        PointState status
    ) =>
        new()
        {
            Code = code,
            Name = name,
            City = city,
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            ConnectorType = connector,
            ConnectorCount = count,
            PowerKW = power,
            PricePerKWh = price,
            Currency = "EUR",
            Status = status,
            IsDemo = true,
        };
}

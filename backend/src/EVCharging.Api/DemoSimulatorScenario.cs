using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Api;

// Uses the same ownership command as a Driver. Never creates a physical transaction.
public static class DemoSimulatorScenario
{
    public const string OtherDriverCreatedAction = "DemoSimulatorDriverCreated";

    public static string OtherDriverEmail(int stationNumber) =>
        $"simulator-{stationNumber:000}@example.test";

    public static async Task PrepareAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken ct = default
    )
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            return;
        if (
            !services.GetRequiredService<IOptions<OcppOptions>>().Value.SimulatorEnabled
            || configuration.GetValue<bool?>("Demo:PrepareOtherDrivers") == false
        )
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        await DemoDataSeeder.ArchiveUnusedLegacyStationsAsync(db, ct);
        var password = configuration["Demo:OtherDriverPassword"];
        if (string.IsNullOrWhiteSpace(password))
            password = configuration["Demo:DriverPassword"];
        if (!string.IsNullOrWhiteSpace(password))
            AccountRules.ValidatePassword(password);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        var charging = scope.ServiceProvider.GetRequiredService<IDriverCharging>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var logger = scope
            .ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DemoSimulatorScenario));

        foreach (var number in Enumerable.Range(4, 7))
        {
            var identity = $"SIM-{number:000}";
            var station = await db.Stations.SingleOrDefaultAsync(
                x => x.Code == identity && x.IsDemo && !x.IsArchived,
                ct
            );
            if (station is null)
                continue; // A custom simulator identity allowlist remains authoritative.
            var email = OtherDriverEmail(number);
            var normalized = AccountRules.NormalizeEmail(email);
            var driver = await db.Accounts.SingleOrDefaultAsync(
                x => x.NormalizedEmail == normalized,
                ct
            );
            if (driver is null)
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    if (configuration.GetValue<bool?>("Demo:PrepareOtherDrivers") == true)
                        throw new InvalidOperationException(
                            "Preparing demo Drivers requires Demo:OtherDriverPassword or Demo:DriverPassword."
                        );
                    continue;
                }
                driver = new Account
                {
                    Email = email,
                    NormalizedEmail = normalized,
                    FirstName = "Simulator",
                    LastName = $"{number:000}",
                    Role = UserRole.Driver,
                };
                driver.PasswordHash = hasher.HashPassword(driver, password);
                db.Accounts.Add(driver);
                db.Audit.Add(
                    new AuditEntry
                    {
                        ActorId = driver.Id,
                        ActorRole = "DemoSetup",
                        EntityId = driver.Id,
                        Action = OtherDriverCreatedAction,
                    }
                );
                await db.SaveChangesAsync(ct);
            }
            else if (
                driver.Role != UserRole.Driver
                || !driver.IsActive
                || driver.ArchivedAt is not null
                || !await db.Audit.AnyAsync(
                    x => x.EntityId == driver.Id && x.Action == OtherDriverCreatedAction,
                    ct
                )
            )
            {
                logger.LogWarning(
                    "Demo Driver setup skipped reserved identity {Identity}: account is not an active scenario account.",
                    identity
                );
                continue;
            }

            var now = clock.GetUtcNow();
            if (
                await db.ChargingSessions.AnyAsync(
                    x => x.PointId == station.Id && x.EndedAt == null,
                    ct
                )
            )
                continue;
            var claims = await db
                .ChargingConnectionRequests.Where(x =>
                    (x.StationId == station.Id || x.DriverId == driver.Id)
                    && (x.State == "Waiting" || x.State == "Linked")
                )
                .ToListAsync(ct);
            if (
                claims.Any(x =>
                    x.DriverId != driver.Id || x.StationId != station.Id || x.State == "Linked"
                )
            )
                continue;
            var waiting = claims.SingleOrDefault(x =>
                x.DriverId == driver.Id && x.StationId == station.Id && x.State == "Waiting"
            );
            if (waiting is not null && waiting.ExpiresAt > now)
                continue;

            try
            {
                // Connect serializes against start/stop and retains all regular ownership constraints.
                await charging.ConnectAsync(driver.Id, station.Id, ct);
            }
            catch (RequestFailure failure) when (failure.Status is 409 or 422)
            {
                // An actual Driver or a transaction can win the race while this scenario is preparing.
                logger.LogInformation(
                    "Demo preparation skipped {Identity}: {Code}.",
                    identity,
                    failure.Code
                );
            }
        }
    }
}

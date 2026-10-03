using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api;

// Explicit, idempotent demo setup. Preserves existing accounts and physical history.
public static class DemoDataSeeder
{
    private static readonly string[] LegacyStationCodes =
    [
        "EV-TIR-001",
        "EV-TIR-002",
        "EV-TIR-003",
        "EV-TIR-004",
        "EV-TIR-005",
        "EV-TIR-006",
        "EV-DUR-001",
        "EV-DUR-002",
        "EV-DUR-003",
        "EV-LUS-001",
    ];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                "Demo seed is available only in Development or Testing."
            );

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

        var provisioner = scope.ServiceProvider.GetRequiredService<OcppSimulatorProvisioner>();
        foreach (var identity in OcppOptions.DefaultSimulatorIdentities)
            await provisioner.EnsureSimulatorAsync(identity, CancellationToken.None);
        await ArchiveUnusedLegacyStationsAsync(db);
        await DemoSimulatorScenario.PrepareAsync(services, configuration);
    }

    public static async Task ArchiveUnusedLegacyStationsAsync(
        ChargingDbContext db,
        CancellationToken ct = default
    )
    {
        var stations = await db
            .Stations.Where(x => x.IsDemo && !x.IsArchived && LegacyStationCodes.Contains(x.Code))
            .ToListAsync(ct);
        foreach (var station in stations)
        {
            // A mapped device or open association is operational data, even on a demo row.
            if (
                await db.OcppConnectors.AnyAsync(x => x.StationId == station.Id, ct)
                || await db.ChargingSessions.AnyAsync(
                    x => x.PointId == station.Id && x.EndedAt == null,
                    ct
                )
                || await db.ChargingConnectionRequests.AnyAsync(
                    x => x.StationId == station.Id && (x.State == "Waiting" || x.State == "Linked"),
                    ct
                )
            )
                continue;
            station.IsArchived = true;
            station.Version++;
        }
        await db.SaveChangesAsync(ct);
    }
}

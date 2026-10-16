using EVCharging.Api;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EVCharging.Tests;

public class DemoDataSeederTests
{
    [Fact]
    public async Task SeedingTwiceKeepsOneSetOfDemoDataAndPreservesAccounts()
    {
        await using var factory = new ApiFactory(simulator: true);
        using var client = factory.CreateClient();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Demo:DriverEmail"] = "demo@example.test",
                    ["Demo:DriverPassword"] = "A-unique-demo-password-2026!",
                    ["Bootstrap:Email"] = "admin@example.test",
                    ["Bootstrap:Password"] = "A-unique-admin-password-2026!",
                }
            )
            .Build();

        await DemoDataSeeder.SeedAsync(factory.Services, config);
        string originalPasswordHash;
        await using (var originalScope = factory.Services.CreateAsyncScope())
        {
            var originalDb = originalScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            originalPasswordHash = (
                await originalDb.Accounts.SingleAsync(x => x.Email == "demo@example.test")
            ).PasswordHash;
        }
        config["Demo:DriverPassword"] = "A-different-demo-password-2026!";
        await DemoDataSeeder.SeedAsync(factory.Services, config);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Equal(
            Enumerable.Range(1, 10).Select(x => $"SIM-{x:000}"),
            await db
                .Stations.Where(x => x.IsDemo && !x.IsArchived)
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToListAsync()
        );
        Assert.Equal(10, await db.OcppChargePoints.CountAsync());
        Assert.Equal(10, await db.ChargingConnectors.CountAsync());
        Assert.Empty(await db.ChargingHistory.ToListAsync());
        Assert.Empty(await db.ChargingSessions.ToListAsync());
        Assert.Equal(8, await db.Accounts.CountAsync(x => x.Role == UserRole.Driver));
        Assert.Single(await db.Accounts.Where(x => x.Role == UserRole.Superadmin).ToListAsync());
        Assert.Equal(
            originalPasswordHash,
            (await db.Accounts.SingleAsync(x => x.Email == "demo@example.test")).PasswordHash
        );
        var claims = await db
            .ChargingConnectionRequests.Where(x => x.State == "Waiting")
            .ToListAsync();
        Assert.Equal(7, claims.Count);
        Assert.Equal(7, claims.Select(x => x.DriverId).Distinct().Count());
        Assert.Equal(
            Enumerable.Range(4, 7).Select(x => $"SIM-{x:000}"),
            await db
                .Stations.Where(x => claims.Select(c => c.StationId).Contains(x.Id))
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToListAsync()
        );
        Assert.All(
            claims,
            x =>
                Assert.InRange(
                    x.ExpiresAt - x.CreatedAt,
                    TimeSpan.FromMinutes(29),
                    TimeSpan.FromMinutes(31)
                )
        );
        Assert.Equal(
            7,
            await db.Audit.CountAsync(x =>
                x.Action == DemoSimulatorScenario.OtherDriverCreatedAction
            )
        );
    }

    [Fact]
    public async Task ArchivesUnusedLegacyFillerAndPreservesHistoryAndOperationalRows()
    {
        await using var factory = new ApiFactory(testMode: true);
        using var client = factory.CreateClient();
        Guid historicalStationId;
        Guid claimedStationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var account = new Account
            {
                Email = "legacy@example.test",
                NormalizedEmail = "LEGACY@EXAMPLE.TEST",
                PasswordHash = "existing",
            };
            var historyStation = new ChargingStation
            {
                Code = "EV-TIR-001",
                Name = "Legacy history",
                IsDemo = true,
            };
            var claimedStation = new ChargingStation
            {
                Code = "EV-TIR-002",
                Name = "Operational demo",
                IsDemo = true,
            };
            var realStation = new ChargingStation { Code = "EV-DUR-001", Name = "Real station" };
            historicalStationId = historyStation.Id;
            claimedStationId = claimedStation.Id;
            db.Accounts.Add(account);
            db.Stations.AddRange(historyStation, claimedStation, realStation);
            db.ChargingHistory.Add(
                new ChargingHistory
                {
                    DriverId = account.Id,
                    StationId = historyStation.Id,
                    Station = historyStation,
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                    EndedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EnergyKWh = 10,
                    UnitPriceSnapshot = 0.35m,
                    TotalCost = 3.5m,
                    IsDemo = true,
                }
            );
            db.ChargingConnectionRequests.Add(
                new ChargingConnectionRequest
                {
                    DriverId = account.Id,
                    StationId = claimedStation.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                }
            );
            await db.SaveChangesAsync();
        }
        var config = Config();
        await DemoDataSeeder.SeedAsync(factory.Services, config);
        await using var readScope = factory.Services.CreateAsyncScope();
        var saved = readScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.True(
            (await saved.Stations.SingleAsync(x => x.Id == historicalStationId)).IsArchived
        );
        Assert.False((await saved.Stations.SingleAsync(x => x.Id == claimedStationId)).IsArchived);
        Assert.False((await saved.Stations.SingleAsync(x => x.Code == "EV-DUR-001")).IsArchived);
        Assert.Single(await saved.ChargingHistory.ToListAsync());
        Assert.Equal(historicalStationId, (await saved.ChargingHistory.SingleAsync()).StationId);
        Assert.Single(
            await saved
                .ChargingConnectionRequests.Where(x => x.StationId == claimedStationId)
                .ToListAsync()
        );
    }

    [Fact]
    public async Task ReservedEmailCollisionNeverCreatesOwnershipForAnExistingAccount()
    {
        await using var factory = new ApiFactory(simulator: true).WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders())
        );
        using var client = factory.CreateClient();
        Guid existingDriverId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var existing = new Account
            {
                Email = DemoSimulatorScenario.OtherDriverEmail(4),
                NormalizedEmail = DemoSimulatorScenario.OtherDriverEmail(4).ToUpperInvariant(),
                PasswordHash = "existing-real-account",
            };
            existingDriverId = existing.Id;
            db.Accounts.Add(existing);
            await db.SaveChangesAsync();
        }
        await DemoSimulatorScenario.PrepareAsync(factory.Services, Config());
        await using var readScope = factory.Services.CreateAsyncScope();
        var saved = readScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Empty(
            await saved
                .ChargingConnectionRequests.Where(x => x.DriverId == existingDriverId)
                .ToListAsync()
        );
        Assert.Equal(
            "existing-real-account",
            (await saved.Accounts.SingleAsync(x => x.Id == existingDriverId)).PasswordHash
        );
        Assert.Equal(
            6,
            await saved.ChargingConnectionRequests.CountAsync(x => x.State == "Waiting")
        );
    }

    [Fact]
    public async Task PreparationRenewsOnlyExpiredScenarioWaitAndPreservesOtherDrivers()
    {
        await using var factory = new ApiFactory(simulator: true);
        using var client = factory.CreateClient();
        var config = Config();
        await DemoSimulatorScenario.PrepareAsync(factory.Services, config);
        Guid otherClaimId;
        Guid expiredClaimId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var fourthStation = await db.Stations.SingleAsync(x => x.Code == "SIM-004");
            var fourthClaim = await db.ChargingConnectionRequests.SingleAsync(x =>
                x.StationId == fourthStation.Id
            );
            expiredClaimId = fourthClaim.Id;
            fourthClaim.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
            fourthClaim.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            var fifthStation = await db.Stations.SingleAsync(x => x.Code == "SIM-005");
            var fifthClaim = await db.ChargingConnectionRequests.SingleAsync(x =>
                x.StationId == fifthStation.Id
            );
            fifthClaim.State = "Cancelled";
            var actualDriver = new Account
            {
                Email = "actual@example.test",
                NormalizedEmail = "ACTUAL@EXAMPLE.TEST",
                PasswordHash = "existing",
            };
            db.Accounts.Add(actualDriver);
            var actualClaim = new ChargingConnectionRequest
            {
                DriverId = actualDriver.Id,
                StationId = fifthStation.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            };
            otherClaimId = actualClaim.Id;
            db.ChargingConnectionRequests.Add(actualClaim);
            await db.SaveChangesAsync();
        }
        await DemoSimulatorScenario.PrepareAsync(factory.Services, config);
        await using var readScope = factory.Services.CreateAsyncScope();
        var saved = readScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Equal(
            "Expired",
            (await saved.ChargingConnectionRequests.SingleAsync(x => x.Id == expiredClaimId)).State
        );
        Assert.Equal(
            "Waiting",
            (await saved.ChargingConnectionRequests.SingleAsync(x => x.Id == otherClaimId)).State
        );
        var renewed = await saved.ChargingConnectionRequests.SingleAsync(x =>
            x.StationId == (saved.Stations.Single(s => s.Code == "SIM-004").Id)
            && x.State == "Waiting"
        );
        Assert.NotEqual(expiredClaimId, renewed.Id);
        Assert.True(renewed.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(29));
    }

    private static IConfigurationRoot Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Demo:DriverEmail"] = "demo@example.test",
                    ["Demo:DriverPassword"] = "A-unique-demo-password-2026!",
                }
            )
            .Build();

    [Fact]
    public async Task ScenarioOptOutAndProductionKeepSimulatorOwnershipUnassigned()
    {
        await using var factory = new ApiFactory(simulator: true);
        using var client = factory.CreateClient();
        var config = Config();
        config["Demo:PrepareOtherDrivers"] = "false";
        await DemoSimulatorScenario.PrepareAsync(factory.Services, config);
        config["Demo:PrepareOtherDrivers"] = "true";
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        environment.EnvironmentName = "Production";
        await DemoSimulatorScenario.PrepareAsync(factory.Services, config);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemoDataSeeder.SeedAsync(factory.Services, config)
        );
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Empty(await db.ChargingConnectionRequests.ToListAsync());
        Assert.Empty(await db.Accounts.ToListAsync());
    }
}

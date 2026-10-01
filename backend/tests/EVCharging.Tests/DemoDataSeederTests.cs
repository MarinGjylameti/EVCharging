using EVCharging.Api;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public class DemoDataSeederTests
{
    [Fact]
    public async Task SeedingTwiceKeepsOneSetOfDemoDataAndPreservesAccounts()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:DriverEmail"] = "demo@example.test",
            ["Demo:DriverPassword"] = "A-unique-demo-password-2026!",
            ["Bootstrap:Email"] = "admin@example.test",
            ["Bootstrap:Password"] = "A-unique-admin-password-2026!"
        }).Build();

        await DemoDataSeeder.SeedAsync(factory.Services, config);
        await DemoDataSeeder.SeedAsync(factory.Services, config);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Equal(10, await db.Stations.CountAsync(x => x.IsDemo));
        Assert.Equal(6, await db.ChargingHistory.CountAsync(x => x.IsDemo));
        Assert.Single(await db.Accounts.Where(x => x.Role == UserRole.Driver).ToListAsync());
        Assert.Single(await db.Accounts.Where(x => x.Role == UserRole.Superadmin).ToListAsync());
    }
}

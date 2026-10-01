using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api.Startup;

public static class StartupCommands
{
    public static async Task<bool> TryRunStartupCommandAsync(this WebApplication app, string[] args)
    {
        if (args.Contains("--migrate"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            await db.Database.MigrateAsync();
            return true;
        }

        if (args.Contains("--bootstrap-superadmin"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<SuperadminBootstrapper>();
            await bootstrapper.BootstrapAsync(
                app.Configuration["Bootstrap:Email"],
                app.Configuration["Bootstrap:Password"]
            );
            return true;
        }

        if (args.Contains("--seed-demo"))
        {
            await DemoDataSeeder.SeedAsync(app.Services, app.Configuration);
            return true;
        }

        return false;
    }
}

using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class StartupConcurrencyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task StartupReloadsAfterConflictsAndDoesNotHideRepeatedFailures(int conflicts)
    {
        var interceptor = new StartupConflict();
        await using var factory = new ApiFactory(simulator: true, saveInterceptor: interceptor);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = await db.OcppChargePoints.SingleAsync(x => x.Identity == "SIM-001");
            await scope
                .ServiceProvider.GetRequiredService<IOcppGateway>()
                .OpenAsync(device.Id, default);
        }
        factory.Services.GetRequiredService<IOptions<OcppOptions>>().Value.SimulatorEnabled = false;
        interceptor.Remaining = conflicts;
        using (var scope = factory.Services.CreateScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<OcppSimulatorProvisioner>();
            if (conflicts == 3)
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                    provisioner.InitializeAsync()
                );
            else
                await provisioner.InitializeAsync();
        }
        Assert.Equal(conflicts, interceptor.Failures);
        // Each failed attempt has distinct entities, rather than repeating stale versions.
        Assert.Equal(
            conflicts,
            interceptor.FailedEntities.Distinct(ReferenceEqualityComparer.Instance).Count()
        );
        using var finalScope = factory.Services.CreateScope();
        var final = finalScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        if (conflicts == 1)
        {
            Assert.Empty(
                await final.OcppConnections.Where(x => x.DisconnectedAt == null).ToListAsync()
            );
            Assert.False(await final.ChargingConnectors.AnyAsync(x => x.IsOnline));
        }
        else
            Assert.Single(
                await final.OcppConnections.Where(x => x.DisconnectedAt == null).ToListAsync()
            );
    }

    private sealed class StartupConflict : SaveChangesInterceptor
    {
        public int Remaining { get; set; }
        public int Failures { get; private set; }
        public List<object> FailedEntities { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var changed =
                data.Context!.ChangeTracker.Entries<EVCharging.Domain.Charging.ChargingConnector>()
                    .FirstOrDefault(x => x.State == EntityState.Modified && !x.Entity.IsOnline);
            if (Remaining > 0 && changed is not null)
            {
                Remaining--;
                Failures++;
                FailedEntities.Add(changed.Entity);
                throw new DbUpdateConcurrencyException(
                    "A competing instance updated this connector."
                );
            }
            return ValueTask.FromResult(result);
        }
    }
}

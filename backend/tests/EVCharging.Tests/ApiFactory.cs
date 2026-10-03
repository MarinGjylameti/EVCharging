using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EVCharging.Tests;

// Test-only storage verifies HTTP/auth behavior, not PostgreSQL constraints or transaction isolation.
public sealed class ApiFactory(
    bool simulator = false,
    bool anonymousSimulators = false,
    string? postgresConnection = null,
    SaveChangesInterceptor? saveInterceptor = null
) : WebApplicationFactory<Program>
{
    private readonly string database = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(
            Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../src/EVCharging.Api")
            )
        );
        builder.ConfigureServices(services =>
        {
            if (simulator)
            {
                services.PostConfigure<OcppOptions>(options =>
                {
                    options.SimulatorEnabled = true;
                    options.AllowAnonymousSimulators = anonymousSimulators;
                    options.SimulatorPassword = "simulator-test-passphrase";
                });
            }
            services.RemoveAll<DbContextOptions<ChargingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ChargingDbContext>>();
            services.RemoveAll<ChargingDbContext>();
            services.AddDbContext<ChargingDbContext>(options =>
            {
                if (postgresConnection is not null)
                    options.UseNpgsql(postgresConnection);
                else
                    options.UseInMemoryDatabase(database).AddInterceptors(new TestTransactionIds());
                if (saveInterceptor is not null)
                    options.AddInterceptors(saveInterceptor);
            });
        });
    }

    // InMemory does not implement PostgreSQL identity columns on non-key properties.
    private sealed class TestTransactionIds : SaveChangesInterceptor
    {
        private static int nextId;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            foreach (
                var entry in data.Context!.ChangeTracker.Entries<OcppChargingTransaction>()
                    .Where(x => x.State == EntityState.Added && x.Entity.Ocpp16TransactionId == 0)
            )
                entry.Entity.Ocpp16TransactionId = Interlocked.Increment(ref nextId);
            return ValueTask.FromResult(result);
        }
    }
}

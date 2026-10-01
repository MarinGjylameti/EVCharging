using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EVCharging.Tests;

// Test-only storage verifies HTTP/auth behavior, not PostgreSQL constraints or transaction isolation.
public sealed class ApiFactory : WebApplicationFactory<Program>
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
            services.RemoveAll<DbContextOptions<ChargingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ChargingDbContext>>();
            services.RemoveAll<ChargingDbContext>();
            services.AddDbContext<ChargingDbContext>(options =>
                options.UseInMemoryDatabase(database)
            );
        });
    }
}

using System.Net;
using System.Net.Http.Json;
using EVCharging.Application.Identity;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class SuperadminBootstrapperTests
{
    [Fact]
    public async Task BootstrapCreatesUsableAdminAndRefusesToReplaceExistingAccount()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        const string password = "A-local-bootstrap-passphrase-2026!";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<SuperadminBootstrapper>();
            await bootstrapper.BootstrapAsync(" First-Admin@example.test ", password);

            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var account = await db.Accounts.SingleAsync();
            var originalHash = account.PasswordHash;
            Assert.Equal("FIRST-ADMIN@EXAMPLE.TEST", account.NormalizedEmail);
            Assert.Equal(UserRole.Superadmin, account.Role);
            Assert.NotEqual(password, originalHash);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bootstrapper.BootstrapAsync("replacement@example.test", "Another-valid-passphrase!")
            );

            var unchanged = await db.Accounts.AsNoTracking().SingleAsync();
            Assert.Equal(account.Id, unchanged.Id);
            Assert.Equal(originalHash, unchanged.PasswordHash);
            Assert.Equal("SuperadminBootstrapped", (await db.Audit.SingleAsync()).Action);
        }

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "first-admin@example.test", password }
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Equal("SUPERADMIN", auth.User.Role);
    }
}

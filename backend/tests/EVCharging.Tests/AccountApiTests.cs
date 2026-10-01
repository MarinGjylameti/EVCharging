using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVCharging.Application.Identity;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class AccountApiTests : IDisposable
{
    private readonly ApiFactory factory = new();
    private HttpClient Client() => factory.CreateClient();
    private static object Registration(string email = "driver@example.com", string language = "sq") =>
        new { email, password = "a-long-driver-passphrase", firstName = "Arta", lastName = "Deda", language };

    private async Task<(HttpClient Client, AuthResult Auth)> Register(string email = "driver@example.com")
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    [Fact]
    public async Task RegistrationStoresHashedCredentialsAndAlwaysCreatesDriver()
    {
        var (client, auth) = await Register();
        Assert.Equal("DRIVER", auth.User.Role);
        Assert.Equal("sq", auth.User.Language);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var user = await db.Accounts.SingleAsync();
        Assert.NotEqual("a-long-driver-passphrase", user.PasswordHash);
        Assert.NotEqual(auth.AccessToken, (await db.Sessions.SingleAsync()).TokenHash);
        Assert.Equal("DriverRegistered", (await db.Audit.SingleAsync()).Action);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/administrators")).StatusCode);
    }

    [Fact]
    public async Task CraftedRoleAndOwnerFieldsAreRejected()
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "attacker@example.com", password = "a-long-driver-passphrase", firstName = "A", lastName = "B", language = "en", role = "SUPERADMIN"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ChargingDbContext>().Accounts.ToListAsync());
    }

    [Fact]
    public async Task DuplicateEmailAndInvalidPasswordAreRejected()
    {
        var (client, _) = await Register();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/register", Registration("DRIVER@EXAMPLE.COM"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "driver@example.com", password = "wrong" })).StatusCode);
    }

    [Fact]
    public async Task LanguagePersistsAndStaleVersionCannotOverwriteSettings()
    {
        var (client, auth) = await Register();
        var response = await client.PatchAsJsonAsync("/api/v1/me/settings", new { language = "en", pushEnabled = true, version = auth.User.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("en", user.Language);
        Assert.True(user.PushEnabled);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PatchAsJsonAsync("/api/v1/me/settings", new { language = "sq", pushEnabled = false, version = auth.User.Version })).StatusCode);
        Assert.Equal("en", (await client.GetFromJsonAsync<UserDto>("/api/v1/me"))!.Language);
    }

    [Fact]
    public async Task LogoutRevokesBearerToken()
    {
        var (client, _) = await Register();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeRevokesOtherSessionsAndDisablesDevices()
    {
        var (client, _) = await Register();
        await client.PostAsJsonAsync("/api/v1/me/devices", new { token = "ExpoPushToken[device123]", platform = "android" });
        var other = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "driver@example.com", password = "a-long-driver-passphrase" });
        var otherAuth = (await other.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/password", new { currentPassword = "a-long-driver-passphrase", newPassword = "a-new-long-driver-passphrase" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", otherAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.All(await db.Sessions.ToListAsync(), x => Assert.NotNull(x.RevokedAt));
        Assert.False((await db.Devices.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task DeactivatedAccountLosesExistingAccess()
    {
        var (client, auth) = await Register();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(x => x.Id == auth.User.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task SuperadminAccessIsEnforcedOnServer()
    {
        var (client, auth) = await Register();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(x => x.Id == auth.User.Id)).Role = UserRole.Superadmin;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/administrators")).StatusCode);
    }

    [Fact]
    public async Task InvalidLanguageAndDeviceCannotBeRegistered()
    {
        var (client, auth) = await Register();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/v1/me/settings", new { language = "de", pushEnabled = false, version = auth.User.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/me/devices", new { token = "not-a-token", platform = "android" })).StatusCode);
    }

    public void Dispose() => factory.Dispose();
}

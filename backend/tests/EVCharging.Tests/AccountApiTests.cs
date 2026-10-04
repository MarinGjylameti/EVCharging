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

    private static object Registration(
        string email = "driver@example.com",
        string language = "sq"
    ) =>
        new
        {
            email,
            password = "a-long-driver-passphrase",
            phone = "+355691234567",
            firstName = "Arta",
            lastName = "Deda",
            language,
        };

    private async Task<(HttpClient Client, AuthResult Auth)> Register(
        string email = "driver@example.com"
    )
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
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
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/admin/access")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/administrators")).StatusCode
        );
    }

    [Fact]
    public async Task CraftedRoleAndOwnerFieldsAreRejected()
    {
        var client = Client();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = "attacker@example.com",
                password = "a-long-driver-passphrase",
                phone = "+355691234567",
                firstName = "A",
                lastName = "B",
                language = "en",
                role = "SUPERADMIN",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .Accounts.ToListAsync()
        );
    }

    [Fact]
    public async Task DuplicateEmailAndInvalidPasswordAreRejected()
    {
        var (client, _) = await Register();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/api/v1/auth/register",
                    Registration("DRIVER@EXAMPLE.COM")
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await client.PostAsJsonAsync(
                    "/api/v1/auth/login",
                    new { email = "driver@example.com", password = "wrong" }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task LanguagePersistsAndStaleVersionCannotOverwriteSettings()
    {
        var (client, auth) = await Register();
        var response = await client.PatchAsJsonAsync(
            "/api/v1/me/settings",
            new
            {
                language = "en",
                pushEnabled = true,
                version = auth.User.Version,
            }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("en", user.Language);
        Assert.True(user.PushEnabled);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (
                await client.PatchAsJsonAsync(
                    "/api/v1/me/settings",
                    new
                    {
                        language = "sq",
                        pushEnabled = false,
                        version = auth.User.Version,
                    }
                )
            ).StatusCode
        );
        Assert.Equal("en", (await client.GetFromJsonAsync<UserDto>("/api/v1/me"))!.Language);
    }

    [Fact]
    public async Task LogoutRevokesBearerToken()
    {
        var (client, _) = await Register();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode
        );
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeRevokesOtherSessionsAndDisablesDevices()
    {
        var (client, _) = await Register();
        await client.PostAsJsonAsync(
            "/api/v1/me/devices",
            new { token = "ExpoPushToken[device123]", platform = "android" }
        );
        var other = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "driver@example.com", password = "a-long-driver-passphrase" }
        );
        var otherAuth = (await other.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await client.PostAsJsonAsync(
                    "/api/v1/auth/password",
                    new
                    {
                        currentPassword = "a-long-driver-passphrase",
                        newPassword = "a-new-long-driver-passphrase",
                    }
                )
            ).StatusCode
        );
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
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/administrators")).StatusCode
        );
    }

    [Fact]
    public async Task InvalidLanguageAndDeviceCannotBeRegistered()
    {
        var (client, auth) = await Register();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PatchAsJsonAsync(
                    "/api/v1/me/settings",
                    new
                    {
                        language = "de",
                        pushEnabled = false,
                        version = auth.User.Version,
                    }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await client.PostAsJsonAsync(
                    "/api/v1/me/devices",
                    new { token = "not-a-token", platform = "android" }
                )
            ).StatusCode
        );
    }

    public void Dispose() => factory.Dispose();

    [Theory]
    [InlineData(" +355 (69) 123-4567 ", "+355691234567")]
    [InlineData("00355 69 1234567", "+355691234567")]
    [InlineData("+4915112345678", "+4915112345678")]
    public async Task RegistrationPersistsContactDetailsAndCreatesAnIndependentDriver(
        string phone,
        string expected
    )
    {
        var client = Client();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                " new@example.com ",
                "a-long-driver-passphrase",
                " Arta ",
                " Deda ",
                "en",
                phone
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Equal("Arta", auth.User.FirstName);
        Assert.Equal("Deda", auth.User.LastName);
        Assert.Equal("new@example.com", auth.User.Email);
        Assert.Equal(expected, auth.User.Phone);
        Assert.Equal("DRIVER", auth.User.Role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        Assert.Equal(expected, (await client.GetFromJsonAsync<UserDto>("/api/v1/me"))!.Phone);
        Assert.Equal("[]", await client.GetStringAsync("/api/v1/driver/history"));
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("NEW@EXAMPLE.COM", "a-long-driver-passphrase")
        );
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(auth.User.Id, (await login.Content.ReadFromJsonAsync<AuthResult>())!.User.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0691234567")]
    [InlineData("+0355691234567")]
    [InlineData("+35569abc4567")]
    [InlineData("+123")]
    [InlineData("+1234567890123456")]
    public async Task InvalidPhoneCannotCreateAnAccountOrSession(string? phone)
    {
        var response = await Client()
            .PostAsJsonAsync(
                "/api/v1/auth/register",
                new RegisterRequest(
                    "new@example.com",
                    "a-long-driver-passphrase",
                    "Arta",
                    "Deda",
                    "sq",
                    phone
                )
            );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_phone", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.False(await db.Accounts.AnyAsync());
        Assert.False(await db.Sessions.AnyAsync());
        Assert.False(await db.Audit.AnyAsync());
    }

    [Theory]
    [InlineData("invalid", "a-long-driver-passphrase", "Arta", "Deda", "sq", "invalid_email")]
    [InlineData("new@example.com", "short", "Arta", "Deda", "sq", "invalid_password")]
    [InlineData("new@example.com", "a-long-driver-passphrase", " ", "Deda", "sq", "invalid_name")]
    [InlineData("new@example.com", "a-long-driver-passphrase", "Arta", " ", "sq", "invalid_name")]
    [InlineData(
        "new@example.com",
        "a-long-driver-passphrase",
        "Arta",
        "Deda",
        "de",
        "invalid_language"
    )]
    public async Task InvalidRegistrationFieldsAreRejected(
        string email,
        string password,
        string first,
        string last,
        string language,
        string code
    )
    {
        var response = await Client()
            .PostAsJsonAsync(
                "/api/v1/auth/register",
                new RegisterRequest(email, password, first, last, language, "+355691234567")
            );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.False(
            await scope.ServiceProvider.GetRequiredService<ChargingDbContext>().Accounts.AnyAsync()
        );
    }

    [Fact]
    public async Task UsableBatteryCapacityPersistsAndCanBeClearedWithoutInventingADefault()
    {
        var (client, auth) = await Register();
        Assert.Null(auth.User.BatteryCapacityKWh);
        var request = new ProfileRequest(
            "Arta",
            "Deda",
            null,
            "VW",
            "ID.4",
            "Lithium-ion",
            auth.User.Version,
            77.25m
        );
        var response = await client.PatchAsJsonAsync("/api/v1/me/profile", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal(77.25m, saved.BatteryCapacityKWh);
        Assert.Equal(
            77.25m,
            (await client.GetFromJsonAsync<UserDto>("/api/v1/me"))!.BatteryCapacityKWh
        );
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (
                await client.PatchAsJsonAsync(
                    "/api/v1/me/profile",
                    request with
                    {
                        BatteryCapacityKWh = 60,
                    }
                )
            ).StatusCode
        );
        var cleared = await client.PatchAsJsonAsync(
            "/api/v1/me/profile",
            request with
            {
                Version = saved.Version,
                BatteryCapacityKWh = null,
            }
        );
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<UserDto>())!.BatteryCapacityKWh);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Null((await db.Accounts.SingleAsync()).BatteryCapacityKWh);
        Assert.Equal(2, await db.Audit.CountAsync(x => x.Action == "ProfileChanged"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1000.01)]
    [InlineData(60.001)]
    public async Task InvalidBatteryCapacityCannotChangeTheProfile(double value)
    {
        var (client, auth) = await Register();
        var response = await client.PatchAsJsonAsync(
            "/api/v1/me/profile",
            new ProfileRequest(
                "Changed",
                "Name",
                null,
                null,
                null,
                null,
                auth.User.Version,
                (decimal)value
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_battery_capacity", await response.Content.ReadAsStringAsync());
        var current = (await client.GetFromJsonAsync<UserDto>("/api/v1/me"))!;
        Assert.Equal("Arta", current.FirstName);
        Assert.Equal(auth.User.Version, current.Version);
        Assert.Null(current.BatteryCapacityKWh);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Api.Endpoints;
using EVCharging.Api.Hosting;
using EVCharging.Api.Security;
using EVCharging.Application.Identity;
using EVCharging.Infrastructure;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class AuthRateLimitTests
{
    // Exercises the production middleware and endpoint mappings, without external
    // listeners, PostgreSQL, background workers or Program's startup commands.
    internal static async Task<WebApplication> Start(int accountLimit = 15, int ipLimit = 300)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddChargingInfrastructure(null);
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.RemoveAll<DbContextOptions<ChargingDbContext>>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<ChargingDbContext>>();
        builder.Services.RemoveAll<ChargingDbContext>();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ChargingDbContext>(options =>
            options.UseInMemoryDatabase(database)
        );
        builder.Services.AddApiSecurity();
        builder.Services.Configure<AuthLimitOptions>(options =>
        {
            options.AccountAttemptsPerMinute = accountLimit;
            options.IpRequestsPerMinute = ipLimit;
        });
        builder.ConfigureApiHttp([]);
        var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                // A test transport override, never a production forwarded-header policy.
                context.Connection.RemoteIpAddress = IPAddress.Parse(
                    context.Request.Headers["Test-Peer-IP"].FirstOrDefault() ?? "192.0.2.1"
                );
                await next(context);
            }
        );
        app.UseApiPipeline();
        app.MapAccountEndpoints();
        app.MapDriverEndpoints();
        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password" });

    private static async Task AssertLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.InRange(response.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task IndependentAccountsCanSignInBehindOneWebServerIp()
    {
        await using var app = await Start();
        using var client = app.GetTestClient();
        for (var i = 0; i < 20; i++)
        {
            using var response = await Login(client, $"driver{i}@example.com");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task NormalizedAccountQuotaIsSharedAcrossLoginRegistrationAndIpAddresses()
    {
        await using var app = await Start();
        using var client = app.GetTestClient();
        for (var i = 0; i < 15; i++)
        {
            client.DefaultRequestHeaders.Remove("Test-Peer-IP");
            client.DefaultRequestHeaders.Add("Test-Peer-IP", $"192.0.2.{i + 1}");
            using var response = await Login(
                client,
                i % 2 == 0 ? "Driver@example.com" : " driver@EXAMPLE.COM "
            );
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var blocked = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                "DRIVER@example.com",
                "a-long-passphrase",
                "A",
                "B",
                "en",
                "+355691234567"
            )
        );
        await AssertLimited(blocked);
        using var scope = app.Services.CreateScope();
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<ChargingDbContext>()
                .Accounts.ToListAsync()
        );
        using var independent = await Login(client, "another@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, independent.StatusCode);
    }

    [Fact]
    public async Task ForwardedHeadersCannotBypassAggregateIpQuota()
    {
        await using var app = await Start(accountLimit: 3, ipLimit: 3);
        using var client = app.GetTestClient();
        for (var i = 0; i < 4; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Remove("Forwarded");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            client.DefaultRequestHeaders.Add("Forwarded", $"for=198.51.100.{i + 1}");
            using var response = await Login(client, "invalid-email");
            if (i < 3)
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            else
                await AssertLimited(response);
        }
    }

    [Fact]
    public async Task PasswordQuotaUsesAuthenticatedAccountAndLogoutStillRevokesSession()
    {
        await using var app = await Start(accountLimit: 2, ipLimit: 20);
        using var client = app.GetTestClient();
        using var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                "driver@example.com",
                "a-long-passphrase",
                "A",
                "B",
                "en",
                "+355691234567"
            )
        );
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var auth = (await registration.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        for (var i = 0; i < 3; i++)
        {
            client.DefaultRequestHeaders.Remove("Test-Peer-IP");
            client.DefaultRequestHeaders.Add("Test-Peer-IP", $"192.0.2.{i + 1}");
            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/password",
                new PasswordRequest("wrong-password", "a-new-long-passphrase")
            );
            if (i < 2)
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            else
                await AssertLimited(response);
        }
        // Exhaust even the aggregate IP bucket. Revocation must remain accessible.
        for (var i = 0; i < 20; i++)
        {
            using var ignored = await Login(client, "invalid-email");
        }
        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var me = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(101, 300)]
    [InlineData(15, 14)]
    [InlineData(15, 10001)]
    public void InvalidLimitsAreRejected(int account, int ip)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApiSecurity();
        builder.Services.Configure<AuthLimitOptions>(options =>
        {
            options.AccountAttemptsPerMinute = account;
            options.IpRequestsPerMinute = ip;
        });
        using var services = builder.Services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<AuthLimitOptions>>().Value
        );
    }
}

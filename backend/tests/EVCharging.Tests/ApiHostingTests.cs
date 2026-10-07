using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EVCharging.Tests;

public sealed class ApiHostingTests
{
    [Fact]
    public void ProductionRefusesMissingDatabaseConfiguration()
    {
        var configuration = new ConfigurationBuilder().Build();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            ApiRuntimeConfiguration.Load(configuration, new TestEnvironment())
        );

        Assert.Equal(
            "Production requires an explicit database connection string.",
            failure.Message
        );
    }

    [Theory]
    [InlineData("http://web.example.test")]
    [InlineData("https://localhost")]
    [InlineData("invalid-origin")]
    public void ProductionRefusesInvalidCorsOrigin(string origin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=db;Database=evcharging",
                    ["Cors:AllowedOrigins:0"] = origin,
                }
            )
            .Build();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            ApiRuntimeConfiguration.Load(configuration, new TestEnvironment())
        );

        Assert.Equal("Production CORS origins must use HTTPS.", failure.Message);
    }

    [Theory]
    [InlineData("/health/live", "alive")]
    [InlineData("/health/ready", "ready")]
    public async Task HealthEndpointsRemainAvailableWithResponseHeaders(string path, string status)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            status,
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("status")
                .GetString()
        );
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "nosniff",
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options"))
        );
    }

    [Theory]
    [InlineData("Ocpp:SimulatorEnabled")]
    [InlineData("Ocpp:AllowAnonymousSimulators")]
    public void ProductionRefusesSimulatorAndAnonymousBypassFlags(string flag)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=db;Database=evcharging",
                    ["Cors:AllowedOrigins:0"] = "https://web.example.test",
                    [flag] = "true",
                }
            )
            .Build();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            ApiRuntimeConfiguration.Load(configuration, new TestEnvironment())
        );
        Assert.Equal("OCPP simulator options are unavailable in Production.", failure.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void NonProductionRetainsExplicitSimulatorConfiguration(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=db;Database=evcharging",
                    ["Ocpp:SimulatorEnabled"] = "true",
                    ["Ocpp:AllowAnonymousSimulators"] = "true",
                }
            )
            .Build();
        Assert.NotNull(
            ApiRuntimeConfiguration.Load(
                configuration,
                new TestEnvironment { EnvironmentName = environment }
            )
        );
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "EVCharging.Api";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

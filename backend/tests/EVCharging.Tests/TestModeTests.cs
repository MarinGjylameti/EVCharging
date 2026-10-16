using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using EVCharging.Api;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class TestModeConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MasterFlagControlsSimulatorDefaults(bool enabled)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Features:TestMode"] = enabled.ToString() }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddChargingInfrastructure("Host=db;Database=test");
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OcppOptions>>().Value;
        Assert.Equal(enabled, options.TestMode);
        Assert.Equal(enabled, options.SimulatorEnabled);
        Assert.Equal(enabled, options.AllowAnonymousSimulators);
    }
}

public sealed partial class OperationsManagementTests
{
    private void UseProductionFeatures()
    {
        factory.Dispose();
        // Deliberately leave legacy simulator settings on: the master must win.
        factory = new ApiFactory(simulator: true, anonymousSimulators: true, testMode: false);
    }

    [Fact]
    public async Task FlagOffKeepsPhysicalCardChargingAndHidesDriverDiagnostics()
    {
        UseProductionFeatures();
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "PROD-CARD");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        await AssignAsync(admin, station.Id, device, driverId, "CARD-001");
        var features = await driver.GetFromJsonAsync<JsonElement>("/api/v1/features");
        Assert.False(features.GetProperty("testMode").GetBoolean());
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/features")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/logs")).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/logs")).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var simulator = await SocketAsync("SIM-001");
        });
        using var socket = await SocketAsync(device.Identity);
        var boot = await CallAsync(
            socket,
            "BootNotification",
            new { chargePointVendor = "Test", chargePointModel = "Physical" }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        var authorization = await CallAsync(socket, "Authorize", new { idTag = "WRONG-CARD" });
        Assert.Equal(
            "Invalid",
            authorization[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var now = DateTimeOffset.UtcNow;
        var transaction = await StartPhysicalAsync(socket, 1, "CARD-001", now);
        await CallAsync(
            socket,
            "MeterValues",
            new
            {
                connectorId = 1,
                transactionId = transaction,
                meterValue = new[]
                {
                    new
                    {
                        timestamp = now,
                        sampledValue = new[]
                        {
                            new
                            {
                                value = "230",
                                measurand = "Voltage",
                                unit = "V",
                            },
                        },
                    },
                },
            }
        );
        var view = await ViewAsync(driver, station.Id);
        Assert.Equal("Card", view.DriverAccessMode);
        Assert.True(view.DeviceConnected);
        Assert.NotNull(view.Session);
        Assert.False(view.CanConnect);
        Assert.Null(view.Diagnostics);
        Assert.Null(view.Session.Readings);
        var stop = await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 2500,
                timestamp = now.AddMinutes(5),
                reason = "Local",
            }
        );
        Assert.Equal(3, stop[0].GetInt32());
        var history = await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history");
        Assert.Equal(1.5m, Assert.Single(history!).EnergyKWh);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.True(await db.OcppMeterSamples.AnyAsync());
        Assert.False(await db.RealtimeOutbox.AnyAsync(x => x.EventType == "DeviceCommandChanged"));
    }

    [Fact]
    public async Task FlagOffRejectsNewAndStoredNoCardDemoAssignments()
    {
        UseProductionFeatures();
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "PROD-NO-CARD");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device/driver",
            new DeviceDriverInput(device.Version, driverId, null)
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "card_required",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString()
        );
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var stored = await db.OcppChargePoints.SingleAsync(x => x.Identity == device.Identity);
            stored.AssignedDriverId = driverId;
            stored.DriverCardIdTag = null;
            await db.SaveChangesAsync();
        }
        using var socket = await SocketAsync(device.Identity);
        var authorization = await CallAsync(socket, "Authorize", new { idTag = "ANY-CARD" });
        Assert.Equal(
            "Invalid",
            authorization[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "ANY-CARD",
                meterStart = 0,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(4, start[0].GetInt32());
        var view = await ViewAsync(driver, station.Id);
        Assert.Null(view.DriverAccessMode);
        Assert.Null(view.Diagnostics);
        Assert.Null(view.Session);
        // A card from the explicit allowlist still works, without a demo owner.
        await StartPhysicalAsync(socket, 1, "CARD-001", DateTimeOffset.UtcNow);
        Assert.Null((await ViewAsync(driver, station.Id)).Session);
    }

    [Fact]
    public async Task FlagOffHidesStoredDemoRowsAndStreamEventsWithoutDeletingThem()
    {
        UseProductionFeatures();
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var physical = await StationAsync(admin, "VISIBLE");
        var demo = await StationAsync(admin, "HIDDEN-DEMO");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var station = await db.Stations.SingleAsync(x => x.Id == demo.Id);
            station.IsDemo = true;
            db.ChargingHistory.Add(
                new ChargingHistory
                {
                    StationId = demo.Id,
                    Station = station,
                    DriverId = driverId,
                    IsDemo = true,
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EndedAt = DateTimeOffset.UtcNow,
                    EnergyKWh = 1,
                    UnitPriceSnapshot = .35m,
                    TotalCost = .35m,
                }
            );
            await db.SaveChangesAsync();
        }
        Assert.Equal(
            physical.Id,
            Assert
                .Single(
                    (await driver.GetFromJsonAsync<DriverStationDto[]>("/api/v1/driver/stations"))!
                )
                .Id
        );
        Assert.Equal(
            physical.Id,
            Assert
                .Single((await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!)
                .Id
        );
        Assert.Empty(
            (await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history"))!
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await driver.GetAsync($"/api/v1/driver/stations/{demo.Id}/charging")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.PostAsync($"/api/v1/driver/stations/{demo.Id}/connect", null)).StatusCode
        );
        using var stream = await driver.GetAsync(
            "/api/v1/driver/stations/events",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));
        var bus = factory.Services.GetRequiredService<StationEventBus>();
        bus.Publish(new(demo.Id, 100, "Changed", EventType: "HiddenDemo"));
        bus.Publish(new(physical.Id, 101, "Changed", driverId, EventType: "DeviceCommandChanged"));
        bus.Publish(new(physical.Id, 102, "Changed", EventType: "VisibleMarker"));
        string lines = "";
        while (!lines.Contains("VisibleMarker", StringComparison.Ordinal))
            lines += await reader.ReadLineAsync(timeout.Token);
        Assert.DoesNotContain("HiddenDemo", lines);
        Assert.DoesNotContain("DeviceCommandChanged", lines);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.True(await verifyDb.Stations.AnyAsync(s => s.Id == demo.Id && s.IsDemo));
        Assert.Single(await verifyDb.ChargingHistory.ToListAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemoDataSeeder.SeedAsync(factory.Services, new ConfigurationBuilder().Build())
        );
        Assert.Contains("Features:TestMode", error.Message);
    }
}

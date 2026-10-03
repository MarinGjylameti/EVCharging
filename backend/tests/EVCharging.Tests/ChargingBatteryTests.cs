using System.Net.Http.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    private async Task<UserDto> CapacityAsync(HttpClient driver, decimal? capacity)
    {
        var user = (await driver.GetFromJsonAsync<UserDto>("/api/v1/me", timeout.Token))!;
        var response = await driver.PatchAsJsonAsync(
            "/api/v1/me/profile",
            new ProfileRequest(
                user.FirstName,
                user.LastName,
                user.Phone,
                user.VehicleMake,
                user.VehicleModel,
                user.BatteryType,
                user.Version,
                capacity
            ),
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>(timeout.Token))!;
    }

    [Fact]
    public async Task VehicleCapacityAndAutomaticMeterSamplesReachOnlyTheOwnerAndRefreshLive()
    {
        using var driver = await DriverAsync();
        using var observer = await DriverAsync("battery-observer@test.com");
        var user = await CapacityAsync(driver, 60);
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var id = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        var receivedBefore = DateTimeOffset.UtcNow;
        await SimulatorMeterAsync(
            socket,
            receivedBefore,
            [
                new
                {
                    value = "1000",
                    measurand = "Energy.Active.Import.Register",
                    unit = "Wh",
                    context = "Sample.Periodic",
                    location = "Outlet",
                },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                    context = "Sample.Periodic",
                    location = "Outlet",
                },
                new
                {
                    value = "45",
                    measurand = "SoC",
                    unit = "Percent",
                    context = "Sample.Periodic",
                    location = "EV",
                },
            ],
            id
        );
        var session = (await ChargingViewAsync(driver)).Session!;
        Assert.Equal(60, session.BatteryCapacityKWh);
        Assert.Equal(45, session.StateOfCharge);
        Assert.Equal(22, session.PowerKW);
        Assert.Equal("Total", session.PowerScope);
        Assert.True(session.StateOfChargeValidUntil >= receivedBefore.AddMinutes(5));

        // Subscribe after initial traffic so the committed profile delta itself
        // must arrive; no new MeterValues or page reload can supply this update.
        var stream = factory.Services.GetRequiredService<IStationEventStream>();
        await using var reader = stream
            .Subscribe(user.Id, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var next = reader.MoveNextAsync().AsTask();
        await CapacityAsync(driver, 80);
        StationChanged change;
        do
        {
            Assert.True(await next);
            change = reader.Current;
            next =
                change.EventType == "SessionVehicleChanged"
                    ? Task.FromResult(false)
                    : reader.MoveNextAsync().AsTask();
        } while (change.EventType != "SessionVehicleChanged");
        Assert.Equal(user.Id, change.RecipientAccountId);
        Assert.Equal(80, change.Session!.BatteryCapacityKWh);
        Assert.True(change.Session.Version > session.Version);
        Assert.Equal(session.Id, change.Session.Id);
        Assert.Equal(80, (await ChargingViewAsync(driver)).Session!.BatteryCapacityKWh);
        Assert.Null((await ChargingViewAsync(observer)).Session);
        var stationsJson = await observer.GetStringAsync("/api/v1/driver/stations", timeout.Token);
        Assert.DoesNotContain("batteryCapacity", stationsJson, StringComparison.OrdinalIgnoreCase);

        await CapacityAsync(driver, null);
        Assert.Null((await ChargingViewAsync(driver)).Session!.BatteryCapacityKWh);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var deltas = await db
            .RealtimeOutbox.Where(x => x.EventType == "SessionVehicleChanged")
            .ToListAsync();
        Assert.Equal(2, deltas.Count);
        Assert.All(
            deltas,
            x =>
            {
                Assert.Equal("Driver", x.Audience);
                Assert.Equal(user.Id, x.RecipientAccountId);
            }
        );
        var stored = await db.ChargingSessions.SingleAsync();
        Assert.NotNull(stored.SocReceivedAt);
        var socDeadline = stored.SocReceivedAt.Value.AddMinutes(5);
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [new { value = "2000", unit = "Wh" }],
            id
        );
        Assert.Equal(
            socDeadline,
            (await ChargingViewAsync(driver)).Session!.StateOfChargeValidUntil
        );
    }

    [Fact]
    public async Task UnclaimedChargeCannotBeAdoptedAndOnlyAnExplicitNewClaimCopiesOwnerCapacity()
    {
        using var driver = await DriverAsync();
        await CapacityAsync(driver, 77);
        using var socket = await ConnectAsync();
        var unowned = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        using (var scope = factory.Services.CreateScope())
            Assert.Null(
                (
                    await scope
                        .ServiceProvider.GetRequiredService<ChargingDbContext>()
                        .ChargingSessions.SingleAsync()
                ).BatteryCapacityKWh
            );
        var station = await MapStationAsync(driver, "SIM-001");
        using var rejected = await driver.PostAsync(
            $"/api/v1/driver/stations/{station.Id}/connect",
            null,
            timeout.Token
        );
        Assert.Equal(System.Net.HttpStatusCode.Conflict, rejected.StatusCode);
        var publicView = await ChargingViewAsync(driver);
        Assert.True(publicView.InUse);
        Assert.Null(publicView.Session);
        Assert.Null(publicView.Diagnostics);
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = unowned,
                meterStop = 0,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        await ClaimAsync(driver);
        await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        Assert.Equal(77, (await ChargingViewAsync(driver)).Session!.BatteryCapacityKWh);
    }
}

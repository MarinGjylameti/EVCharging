using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    private async Task<DriverStationDto> MapStationAsync(HttpClient client, string identity) =>
        Assert.Single(
            (
                await client.GetFromJsonAsync<DriverStationDto[]>(
                    "/api/v1/driver/stations",
                    timeout.Token
                )
            )!,
            x => x.Code == identity
        );

    [Fact]
    public async Task AnotherDriverReceivesOnlyThePublicEstimateAndProfileChangesUpdateItImmediately()
    {
        using var owner = await DriverAsync();
        using var observer = await DriverAsync("map-estimate-observer@test.com");
        var observerUser = (await observer.GetFromJsonAsync<UserDto>("/api/v1/me", timeout.Token))!;
        await CapacityAsync(owner, 77);
        using var socket = await ConnectAsync("SIM-009");
        await ClaimAsync(owner, "SIM-009");
        var transaction = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        var stream = factory.Services.GetRequiredService<IStationEventStream>();
        await using var reader = stream
            .Subscribe(observerUser.Id, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var next = reader.MoveNextAsync().AsTask();
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
                new
                {
                    value = "50",
                    measurand = "SoC",
                    unit = "Percent",
                    location = "EV",
                },
            ],
            transaction
        );
        StationChanged delta;
        do
        {
            Assert.True(await next);
            delta = reader.Current;
            if (delta.Station?.AvailabilityForecast is not null)
                break;
            next = reader.MoveNextAsync().AsTask();
        } while (true);
        Assert.Null(delta.RecipientAccountId);
        Assert.Null(delta.Session);
        Assert.Equal("Calculated", delta.Station!.AvailabilityForecast!.Source);
        Assert.InRange(
            (
                delta.Station.AvailabilityForecast.EstimatedAvailableAt
                - delta.Station.AvailabilityForecast.ObservedAt
            ).TotalMinutes,
            104.99,
            105.01
        );
        Assert.Null((await ChargingViewAsync(observer, "SIM-009")).Session);
        await CapacityAsync(owner, 60);
        var updated = (await MapStationAsync(observer, "SIM-009")).AvailabilityForecast!;
        Assert.InRange(
            (updated.EstimatedAvailableAt - updated.ObservedAt).TotalMinutes,
            81.8,
            81.9
        );
        await CapacityAsync(owner, null);
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        await CapacityAsync(owner, 77);
        await StatusAsync(socket, "SuspendedEV");
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        await StatusAsync(socket, "Charging");
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [
                new
                {
                    value = "11",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transaction
        );
        Assert.NotNull((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        var json = await observer.GetStringAsync("/api/v1/driver/stations", timeout.Token);
        Assert.DoesNotContain("batteryCapacity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stateOfCharge", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("map-estimate-observer", json);
    }

    [Fact]
    public async Task AutomaticSimulatorPublishesItsTargetDurationWithoutClaimingTheViewersAccount()
    {
        using var observer = await DriverAsync();
        using var socket = await ConnectAsync("SIM-009");
        var transaction = await SimulatorStartAsync(socket, DateTimeOffset.UtcNow);
        await StatusAsync(socket, "Charging");
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
                new
                {
                    value = "50",
                    measurand = "SoC",
                    unit = "Percent",
                },
            ],
            transaction
        );
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        async Task<JsonElement> Estimate(
            int id,
            int seconds,
            string vendor = "EVCharging.Simulator"
        ) =>
            await CallAsync(
                socket,
                "DataTransfer",
                new
                {
                    vendorId = vendor,
                    messageId = "ChargingEstimate",
                    data = JsonSerializer.Serialize(
                        new { transactionId = id, remainingSeconds = seconds }
                    ),
                }
            );
        Assert.Equal(
            "Accepted",
            (await Estimate(transaction, 120))[2].GetProperty("status").GetString()
        );
        var station = await MapStationAsync(observer, "SIM-009");
        var forecast = station.AvailabilityForecast!;
        Assert.Equal("Simulator", forecast.Source);
        Assert.Equal(120, (forecast.EstimatedAvailableAt - forecast.ObservedAt).TotalSeconds);
        Assert.Equal(forecast.ObservedAt.AddSeconds(120), forecast.ValidUntil);
        Assert.Null((await ChargingViewAsync(observer, "SIM-009")).Session);
        Assert.Null((await MapStationAsync(observer, "SIM-001")).AvailabilityForecast);
        Assert.Equal(
            "Rejected",
            (await Estimate(transaction + 10000, 100))[2].GetProperty("status").GetString()
        );
        Assert.Equal(
            "UnknownVendorId",
            (await Estimate(transaction, 100, "Unknown"))[2].GetProperty("status").GetString()
        );
        Assert.Equal(4, (await Estimate(transaction, 86401))[0].GetInt32());
        Assert.Equal(
            station.AvailabilityForecast,
            (await MapStationAsync(observer, "SIM-009")).AvailabilityForecast
        );
        await Estimate(transaction, 0);
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        await Estimate(transaction, 90);
        await StatusAsync(socket, "SuspendedEV");
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        await StatusAsync(socket, "Charging");
        await SimulatorMeterAsync(
            socket,
            DateTimeOffset.UtcNow,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transaction
        );
        await Estimate(transaction, 90);
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 0,
                timestamp = DateTimeOffset.UtcNow,
                reason = "Local",
            }
        );
        Assert.Null((await MapStationAsync(observer, "SIM-009")).AvailabilityForecast);
        Assert.Equal(
            "Rejected",
            (await Estimate(transaction, 100))[2].GetProperty("status").GetString()
        );
    }
}

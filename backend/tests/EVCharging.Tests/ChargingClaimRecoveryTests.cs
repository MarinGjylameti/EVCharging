using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    [Fact]
    public async Task BlockingConnectionIdentifiesOnlyOwnWaitingRequestAndCancelReleasesIt()
    {
        using var driver = await DriverAsync();
        using var other = await DriverAsync("blocker-other@test.com");
        var stations = (
            await driver.GetFromJsonAsync<DriverStationDto[]>(
                "/api/v1/driver/stations",
                timeout.Token
            )
        )!;
        var first = stations.Single(x => x.Code == "SIM-001");
        var next = stations.Single(x => x.Code == "SIM-002");
        await ClaimAsync(driver);
        var blocked = await ReadChargingAsync(driver, next.Id);
        var waiting = Assert.IsType<BlockingChargingConnection>(blocked.BlockingConnection);
        Assert.Equal(first.Id, waiting.StationId);
        Assert.Equal(first.Name, waiting.StationName);
        Assert.Equal(1, waiting.ConnectorId);
        Assert.Equal("Waiting", waiting.State);
        Assert.Null(blocked.Session);
        Assert.Null((await ReadChargingAsync(other, next.Id)).BlockingConnection);
        Assert.Null((await ReadChargingAsync(driver, first.Id)).BlockingConnection);

        (
            await driver.PostAsync(
                $"/api/v1/driver/stations/{first.Id}/cancel",
                null,
                timeout.Token
            )
        ).EnsureSuccessStatusCode();
        Assert.Null((await ReadChargingAsync(driver, next.Id)).BlockingConnection);
        await ClaimAsync(driver, "SIM-002");
    }

    [Fact]
    public async Task ExpiredWaitingDoesNotBlockButDisconnectedLinkedSessionRequiresConfirmedStop()
    {
        using var driver = await DriverAsync();
        var stations = (
            await driver.GetFromJsonAsync<DriverStationDto[]>(
                "/api/v1/driver/stations",
                timeout.Token
            )
        )!;
        var first = stations.Single(x => x.Code == "SIM-001");
        var next = stations.Single(x => x.Code == "SIM-002");
        await ClaimAsync(driver);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var claim = await db.ChargingConnectionRequests.SingleAsync();
            claim.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
            claim.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Null((await ReadChargingAsync(driver, next.Id)).BlockingConnection);
        await ClaimAsync(driver, "SIM-002");
        using var socket = await ConnectAsync("SIM-002");
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 0,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "lost connection",
            timeout.Token
        );
        var blocked = await ReadChargingAsync(driver, first.Id);
        Assert.Equal("Linked", blocked.BlockingConnection?.State);
        Assert.Equal(next.Id, blocked.BlockingConnection?.StationId);
        // Cancelling a waiting request must never silently end a started session.
        var afterCancel = await (
            await driver.PostAsync($"/api/v1/driver/stations/{next.Id}/cancel", null, timeout.Token)
        ).Content.ReadFromJsonAsync<DriverStationCharging>(timeout.Token);
        Assert.NotNull(afterCancel?.Session);
        Assert.Null(afterCancel.Session.StoppedAt);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await driver.PostAsync(
                    $"/api/v1/driver/stations/{first.Id}/connect",
                    null,
                    timeout.Token
                )
            ).StatusCode
        );

        using var reconnected = await ConnectAsync("SIM-002");
        await CallAsync(
            reconnected,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 1000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Null((await ReadChargingAsync(driver, first.Id)).BlockingConnection);
        await ClaimAsync(driver);
        await reconnected.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    private async Task<DriverStationCharging> ReadChargingAsync(
        HttpClient driver,
        Guid stationId
    ) =>
        (
            await driver.GetFromJsonAsync<DriverStationCharging>(
                $"/api/v1/driver/stations/{stationId}/charging?connector=1",
                timeout.Token
            )
        )!;
}

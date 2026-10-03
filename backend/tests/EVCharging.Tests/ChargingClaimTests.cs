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
    public async Task ExplicitClaimBeforeStartPreservesPhysicalTimeMeasurementsAndOriginalTariffAndSurvivesAppReopen()
    {
        using var driver = await DriverAsync();
        using var other = await DriverAsync("late-other@test.com");
        using var socket = await ConnectAsync("SIM-003");
        await ClaimAsync(driver, "SIM-003");
        var started = DateTimeOffset.UtcNow.AddMinutes(-4);
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 1000,
                timestamp = started,
            }
        );
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        await StatusAsync(socket, "Charging", started.AddSeconds(1));
        await CallAsync(
            socket,
            "MeterValues",
            new
            {
                connectorId = 1,
                transactionId,
                meterValue = new[]
                {
                    new
                    {
                        timestamp = started.AddMinutes(3),
                        sampledValue = new[] { new { value = "3000", unit = "Wh" } },
                    },
                },
            }
        );
        Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
        Guid sessionId;
        DateTimeOffset received;
        Guid stationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var session = await db.ChargingSessions.SingleAsync();
            sessionId = session.Id;
            received = session.StartReceivedAt;
            stationId = session.PointId;
            Assert.Equal(.35m, session.UnitPriceSnapshot);
            (await db.Stations.SingleAsync(x => x.Id == stationId)).PricePerKWh = .40m;
            await db.SaveChangesAsync();
        }
        await ClaimAsync(driver, "SIM-003");
        await ClaimAsync(driver, "SIM-003");
        using var reopened = factory.CreateClient();
        reopened.DefaultRequestHeaders.Authorization = driver.DefaultRequestHeaders.Authorization;
        var state = (
            await reopened.GetFromJsonAsync<DriverChargingState>(
                "/api/v1/driver/charging-sessions",
                timeout.Token
            )
        )!;
        var current = Assert.Single(state.Sessions);
        Assert.Equal(sessionId, current.Id);
        Assert.Equal(started, current.StartedAt);
        Assert.Equal(received, current.StartReceivedAt);
        Assert.Equal(2m, current.EnergyKWh);
        Assert.Equal(.35m, current.UnitPricePerKWh);
        Assert.Equal(.70m, current.Cost);
        var conflict = await other.PostAsync(
            $"/api/v1/driver/stations/{stationId}/connect",
            null,
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var claim = await db.ChargingConnectionRequests.SingleAsync();
            Assert.Equal("Linked", claim.State);
            Assert.True(claim.LinkedAt > started);
            Assert.Equal(sessionId, claim.SessionId);
            Assert.Single(await db.ChargingSessions.ToListAsync());
        }
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 4000,
                timestamp = started.AddMinutes(5),
            }
        );
        await ClaimAsync(driver, "SIM-002"); // Completion releases the one-Driver invariant.
        var history = Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        Assert.Equal(1.05m, history.TotalCost);
        Assert.Equal(.35m, history.UnitPriceSnapshot);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task OneDriverCannotClaimAnotherPointWhileWaitingOrLinkedAndExpiredWaitingCanMove()
    {
        using var driver = await DriverAsync();
        var stations = (
            await driver.GetFromJsonAsync<DriverStationDto[]>(
                "/api/v1/driver/stations",
                timeout.Token
            )
        )!;
        var second = stations.Single(x => x.Code == "SIM-002");
        await ClaimAsync(driver);
        async Task AssertConflictAsync()
        {
            var response = await driver.PostAsync(
                $"/api/v1/driver/stations/{second.Id}/connect",
                null,
                timeout.Token
            );
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("driver_already_connected", await response.Content.ReadAsStringAsync());
        }
        await AssertConflictAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var waiting = await db.ChargingConnectionRequests.SingleAsync();
            waiting.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
            waiting.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await ClaimAsync(driver, "SIM-002");
        using var socket = await ConnectAsync("SIM-002");
        await CallAsync(
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
        var first = stations.Single(x => x.Code == "SIM-001");
        var linkedConflict = await driver.PostAsync(
            $"/api/v1/driver/stations/{first.Id}/connect",
            null,
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.Conflict, linkedConflict.StatusCode);
        Assert.Contains(
            "driver_already_connected",
            await linkedConflict.Content.ReadAsStringAsync()
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
    }
}

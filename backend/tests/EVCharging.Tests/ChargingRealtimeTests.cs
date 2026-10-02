using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    [Fact]
    public async Task PowerIsReportedOnlyAndClearedAcrossPauseAndStaleReadings()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync("SIM-003");
        await ClaimAsync(driver, "SIM-003");
        var started = DateTimeOffset.UtcNow.AddMinutes(-68);
        await StatusAsync(socket, "Charging", started.AddSeconds(2));
        var start = await CallAsync(socket, "StartTransaction", new { connectorId=1, idTag="TAG-001", meterStart=0, timestamp=started });
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        async Task<DriverChargingSession> SnapshotAsync() => Assert.Single((await driver.GetFromJsonAsync<DriverChargingState>("/api/v1/driver/charging-sessions", timeout.Token))!.Sessions);
        async Task MeterAsync(int seconds, object[] sampledValue) => await CallAsync(socket, "MeterValues", new
        { connectorId=1, transactionId, meterValue=new[] { new { timestamp=started.AddSeconds(seconds), sampledValue } } });
        await MeterAsync(30, [new { value="183", measurand="Energy.Active.Import.Register", unit="Wh" }]);
        await MeterAsync(60, [new { value="367", measurand="Energy.Active.Import.Register", unit="Wh" }]);
        Assert.Null((await SnapshotAsync()).PowerKW);
        await MeterAsync(90, [new { value="21500", measurand="Power.Active.Import", unit="W" }]);
        var reported = await SnapshotAsync();
        Assert.Equal("Charging", reported.Status);
        Assert.Equal(21.5m, reported.PowerKW);
        Assert.Equal("Reported", reported.PowerSource);
        await StatusAsync(socket, "SuspendedEV", started.AddSeconds(91));
        Assert.Null((await SnapshotAsync()).PowerKW);
        await StatusAsync(socket, "Charging", started.AddSeconds(92));
        Assert.Null((await SnapshotAsync()).PowerKW);
        await MeterAsync(120, [new { value="22", measurand="Power.Active.Import", unit="kW" }]);
        Assert.Equal(22m, (await SnapshotAsync()).PowerKW);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.ChargingSessions.SingleAsync()).PowerReceivedAt=DateTimeOffset.UtcNow.AddMinutes(-3);
            await db.SaveChangesAsync();
        }
        Assert.Null((await SnapshotAsync()).PowerKW);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
    [Fact]
    public async Task ManualSimulatorSocWithDefaultPhaseBindsOnlyTheActiveConnectorSession()
    {
        using var driver = await DriverAsync();
        using var otherDriver = await DriverAsync("other-soc@test.com");
        using var socket = await ConnectAsync("SIM-003");
        await ClaimAsync(driver, "SIM-003");
        var started = DateTimeOffset.UtcNow.AddMinutes(-68);
        await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 0,
                timestamp = started,
            }
        );
        await StatusAsync(socket, "Charging", started.AddSeconds(1));
        await CallAsync(
            socket,
            "MeterValues",
            new
            {
                connectorId = 1,
                meterValue = new[]
                {
                    new
                    {
                        timestamp = started.AddSeconds(30),
                        sampledValue = new[]
                        {
                            new
                            {
                                value = "100",
                                measurand = "SoC",
                                unit = "Percent",
                                phase = "L1",
                                location = "Outlet",
                            },
                        },
                    },
                },
            }
        );
        var session = Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
        Assert.Equal(100m, session.StateOfCharge);
        Assert.Equal("Charging", session.Status);
        Assert.Null(session.StoppedAt);
        Assert.Empty(
            (
                await otherDriver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var sample = await db.OcppMeterSamples.SingleAsync();
            Assert.Equal(session.Id, sample.TransactionId);
            Assert.Equal("L1", sample.Phase); // Original device telemetry is retained.
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task Sim003StreamRefreshesStartMeterPauseFaultStopAndNextSessionWithOldClock()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync("SIM-003");
        await ClaimAsync(driver, "SIM-003");
        var station = Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverStationDto[]>(
                    "/api/v1/driver/stations",
                    timeout.Token
                )
            )!,
            x => x.Code == "SIM-003"
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/driver/stations/events"
        );
        using var response = await driver.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(timeout.Token)
        );
        while (await reader.ReadLineAsync(timeout.Token) != "event: ready") { }

        // Just like the page, request the authorized snapshot only when an event
        // names this station. There is no timer, manual refresh or navigation.
        async Task<DriverChargingSession> OnChangeAsync(Func<DriverChargingSession, bool> expected)
        {
            while (true)
            {
                if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                    continue;
                var line = await reader.ReadLineAsync(timeout.Token);
                Assert.StartsWith("data: ", line);
                using var json = JsonDocument.Parse(line![6..]);
                Assert.False(json.RootElement.TryGetProperty("recipientAccountId", out _));
                if (json.RootElement.GetProperty("stationId").GetGuid() != station.Id)
                    continue;
                var snapshot = (
                    await driver.GetFromJsonAsync<DriverStationCharging>(
                        $"/api/v1/driver/stations/{station.Id}/charging",
                        timeout.Token
                    )
                )!;
                if (snapshot.Session is { } session && expected(session))
                    return session;
            }
        }

        var started = DateTimeOffset.UtcNow.AddMinutes(-68);
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 0,
                timestamp = started,
            }
        );
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        var first = await OnChangeAsync(x => x.Status == "Connecting");
        await StatusAsync(socket, "Charging", started.AddSeconds(1));
        await OnChangeAsync(x => x.Status == "Charging");
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
                        timestamp = started.AddMinutes(10),
                        sampledValue = new[]
                        {
                            new
                            {
                                value = "1500",
                                measurand = "Energy.Active.Import.Register",
                                unit = "Wh",
                            },
                        },
                    },
                },
            }
        );
        await OnChangeAsync(x => x.EnergyKWh == 1.5m);
        var minute = 11;
        foreach (
            var status in new[]
            {
                "SuspendedEV",
                "SuspendedEVSE",
                "Faulted",
                "Unavailable",
                "Charging",
            }
        )
        {
            await StatusAsync(socket, status, started.AddMinutes(minute++));
            var session = await OnChangeAsync(x => x.Status == OcppStationProjection.Map(status).ToString());
            Assert.Equal(first.Id, session.Id);
            Assert.Equal(1.5m, session.EnergyKWh);
        }
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 2000,
                timestamp = started.AddMinutes(20),
            }
        );
        var stopped = await OnChangeAsync(x => x.Status == "Completed");
        Assert.Equal(2m, stopped.EnergyKWh);
        Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );

        await ClaimAsync(driver, "SIM-003");
        // Restarting with the simulator's unchanged, old start time must reset
        // ordering for the new run, even after the prior clock advanced.
        await StatusAsync(socket, "Charging", started.AddSeconds(2));
        await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 0,
                timestamp = started,
            }
        );
        var next = await OnChangeAsync(x => x.Status == "Charging" && x.Id != first.Id);
        Assert.Equal(0m, next.EnergyKWh);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await OnChangeAsync(x => x.Status == "ConnectionLost");
    }

    [Fact]
    public async Task ConnectorChangesNotifyEvenWhenThePublicStationStateDoesNotChange()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        var observed = DateTimeOffset.UtcNow;
        await StatusAsync(socket, "SuspendedEV", observed);
        Assert.Equal("Occupied", await StationStatusAsync(driver, "SIM-001"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var before = await db.RealtimeOutbox.CountAsync(x => x.EventType == "ConnectorChanged");
        await StatusAsync(socket, "SuspendedEVSE", observed.AddSeconds(1));
        Assert.Equal("Occupied", await StationStatusAsync(driver, "SIM-001"));
        Assert.Equal(
            before + 1,
            await db.RealtimeOutbox.CountAsync(x => x.EventType == "ConnectorChanged")
        );
        await StatusAsync(socket, "SuspendedEV", observed.AddSeconds(-1));
        Assert.Equal(
            before + 1,
            await db.RealtimeOutbox.CountAsync(x => x.EventType == "ConnectorChanged")
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task SimulatorClockExceptionDoesNotAcceptStaleOrFutureStatusForRealStations()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var station = await db.Stations.SingleAsync(x => x.Code == "SIM-001");
        station.IsDemo = false;
        await db.SaveChangesAsync();
        var device = await db.OcppChargePoints.SingleAsync(x => x.Identity == "SIM-001");
        var projection = scope.ServiceProvider.GetRequiredService<OcppStationProjection>();
        foreach (
            var time in new[]
            {
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(1),
            }
        )
            await projection.SetConnectorAsync(device.Id, 1, "Charging", time, timeout.Token);
        await db.SaveChangesAsync();
        Assert.Null(
            (await db.OcppConnectors.SingleAsync(x => x.ChargePointId == device.Id)).ReportedStatus
        );
        await projection.SetConnectorAsync(
            device.Id,
            1,
            "Available",
            DateTimeOffset.UtcNow,
            timeout.Token
        );
        await db.SaveChangesAsync();
        Assert.Equal(
            "Available",
            (await db.OcppConnectors.SingleAsync(x => x.ChargePointId == device.Id)).ReportedStatus
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}

public sealed class StationEventSignalTests
{
    [Fact]
    public async Task NotificationsCoalesceAndWakeWithoutWaitingForTheRecoveryTimer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var signal = new StationEventSignal();
        for (var i = 0; i < 100; i++)
            signal.Notify();
        var first = signal.WaitAsync(deadline.Token);
        Assert.True(first.IsCompletedSuccessfully);
        await first;
        var next = signal.WaitAsync(deadline.Token);
        Assert.False(next.IsCompleted);
        signal.Notify();
        await next;
    }
}


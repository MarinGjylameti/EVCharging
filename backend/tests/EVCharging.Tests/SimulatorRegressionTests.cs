using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OcppWebSocketTests
{
    private async Task<DriverStationCharging> ChargingViewAsync(
        HttpClient driver,
        string identity = "SIM-001"
    )
    {
        var station = Assert.Single(
            (
                await driver.GetFromJsonAsync<DriverStationDto[]>(
                    "/api/v1/driver/stations",
                    timeout.Token
                )
            )!,
            x => x.Code == identity
        );
        return (
            await driver.GetFromJsonAsync<DriverStationCharging>(
                $"/api/v1/driver/stations/{station.Id}/charging",
                timeout.Token
            )
        )!;
    }

    private async Task<int> SimulatorStartAsync(
        WebSocket socket,
        DateTimeOffset started,
        int meterStart = 0
    )
    {
        var response = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart,
                timestamp = started,
            }
        );
        Assert.Equal(3, response[0].GetInt32());
        return response[2].GetProperty("transactionId").GetInt32();
    }

    private async Task SimulatorMeterAsync(
        WebSocket socket,
        DateTimeOffset observed,
        object[] samples,
        int? transactionId = null
    )
    {
        var payload = new Dictionary<string, object>
        {
            ["connectorId"] = 1,
            ["meterValue"] = new[] { new { timestamp = observed, sampledValue = samples } },
        };
        if (transactionId is { } id)
            payload["transactionId"] = id;
        var response = await CallAsync(socket, "MeterValues", payload);
        Assert.Equal(3, response[0].GetInt32());
    }

    [Fact]
    public async Task RecordedSimulatorTraceKeepsTheActiveSessionWhenNoStopWasSent()
    {
        using var driver = await DriverAsync();
        using var other = await DriverAsync("trace-observer@test.com");
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var deviceTime = DateTimeOffset.UtcNow.AddMinutes(-68);
        var transactionId = await SimulatorStartAsync(socket, deviceTime);
        await StatusAsync(socket, "Charging", deviceTime.AddSeconds(2));
        await SimulatorMeterAsync(
            socket,
            deviceTime.AddMinutes(5),
            [
                new
                {
                    value = "2383",
                    phase = "L1",
                    unit = "Wh",
                },
            ],
            transactionId
        );
        var original = (await ChargingViewAsync(driver)).Session!;

        // The attached traffic shows repeated rejected starts followed by a local
        // "StopTransaction" animation log, but no OUT StopTransaction frame.
        var repeated = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 0,
                timestamp = deviceTime,
            }
        );
        Assert.Equal(4, repeated[0].GetInt32());
        Assert.Equal("OccurrenceConstraintViolation", repeated[2].GetString());
        Assert.Contains(transactionId.ToString(), repeated[3].GetString());
        await StatusAsync(socket, "SuspendedEV", deviceTime.AddMinutes(55));
        var paused = await ChargingViewAsync(driver);
        Assert.Equal(original.Id, paused.Session!.Id);
        Assert.Equal("PausedByVehicle", paused.Session.Status);
        Assert.Equal(2.383m, paused.Session.EnergyKWh);
        Assert.Null(paused.Session.StoppedAt);
        Assert.Null(paused.Session.PowerKW);
        Assert.Equal(transactionId.ToString(), paused.Diagnostics!.ExternalSessionId);
        Assert.True(paused.Diagnostics.LastCommand!.Accepted);
        Assert.Equal("StatusNotification", paused.Diagnostics.LastCommand.Action);
        Assert.False(paused.Diagnostics.LastFailure!.Accepted);
        Assert.Equal("StartTransaction", paused.Diagnostics.LastFailure.Action);
        Assert.Equal("OccurrenceConstraintViolation", paused.Diagnostics.LastFailure.FailureCode);
        Assert.Empty(
            (
                await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        var privateView = await ChargingViewAsync(other);
        Assert.Null(privateView.Session);
        Assert.Null(privateView.Diagnostics);
        Assert.True(privateView.InUse);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Single(await db.ChargingSessions.ToListAsync(timeout.Token));
            var transaction = await db.OcppChargingTransactions.SingleAsync(timeout.Token);
            Assert.Null(transaction.StopMessageId);
            Assert.Null(transaction.StoppedAt);
            Assert.Contains(
                await db.RealtimeOutbox.ToListAsync(timeout.Token),
                x =>
                    x.EventType == "DeviceCommandChanged"
                    && x.Audience == "Driver"
                    && JsonSerializer.Deserialize<StationChanged>(x.PayloadJson)?.Command
                        is { Accepted: false, FailureCode: "OccurrenceConstraintViolation" }
            );
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task EverySimulatorStatusAndErrorReflectsEvenWhenTheFormTimestampIsReused()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var fixedTime = DateTimeOffset.UtcNow.AddMinutes(-68);
        await SimulatorStartAsync(socket, fixedTime);
        await SimulatorMeterAsync(
            socket,
            fixedTime.AddMinutes(10),
            [new { value = "2383", unit = "Wh" }]
        );
        var statuses = new (string Device, string Session)[]
        {
            ("Available", "Available"),
            ("Preparing", "Connecting"),
            ("Charging", "Charging"),
            ("SuspendedEV", "PausedByVehicle"),
            ("SuspendedEVSE", "PausedByCharger"),
            ("Finishing", "Finishing"),
            ("Reserved", "Reserved"),
            ("Unavailable", "Unavailable"),
            ("Faulted", "Faulted"),
        };
        foreach (var status in statuses)
        {
            Assert.Equal(3, (await StatusAsync(socket, status.Device, fixedTime))[0].GetInt32());
            var state = await ChargingViewAsync(driver);
            Assert.Equal(status.Session, state.Session!.Status);
            Assert.Equal(status.Session, state.Connector!.Status);
            Assert.Equal(2.383m, state.Session.EnergyKWh);
            Assert.Null(state.Session.StoppedAt);
        }

        var errorCodes = new[]
        {
            "ConnectorLockFailure",
            "EVCommunicationError",
            "GroundFailure",
            "HighTemperature",
            "InternalError",
            "LocalListConflict",
            "OtherError",
            "OverCurrentFailure",
            "OverVoltage",
            "PowerMeterFailure",
            "PowerSwitchFailure",
            "ReaderFailure",
            "ResetFailure",
            "UnderVoltage",
            "WeakSignal",
        };
        await StatusAsync(socket, "Charging", fixedTime);
        await SimulatorMeterAsync(
            socket,
            fixedTime,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ]
        );
        foreach (var errorCode in errorCodes)
        {
            var response = await CallAsync(
                socket,
                "StatusNotification",
                new
                {
                    connectorId = 1,
                    status = "Charging",
                    errorCode,
                    info = "Simulator fault detail",
                    timestamp = fixedTime,
                }
            );
            Assert.Equal(3, response[0].GetInt32());
            var state = await ChargingViewAsync(driver);
            Assert.Equal("Charging", state.Session!.Status);
            Assert.Equal(errorCode, state.Session.FaultCode);
            Assert.Equal("Simulator fault detail", state.Session.FaultInfo);
            Assert.Equal(errorCode, state.Connector!.FaultCode);
            Assert.Equal(22m, state.Session.PowerKW);
            Assert.Equal("Charging", state.Station.Status);
            Assert.Equal(errorCode, state.Station.WarningCode);
            await StatusAsync(socket, "Charging", fixedTime);
            var cleared = await ChargingViewAsync(driver);
            Assert.Equal("Charging", cleared.Session!.Status);
            Assert.Null(cleared.Session.FaultCode);
            Assert.Null(cleared.Session.FaultInfo);
            Assert.Null(cleared.Connector!.FaultCode);
            Assert.Null(cleared.Station.WarningCode);
        }

        var invalid = await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Charging",
                errorCode = "ImaginaryError",
                timestamp = fixedTime,
            }
        );
        Assert.Equal(4, invalid[0].GetInt32());
        Assert.Equal("PropertyConstraintViolation", invalid[2].GetString());
        Assert.Equal("Charging", (await ChargingViewAsync(driver)).Session!.Status);
        var oversizedInfo = await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Charging",
                errorCode = "GroundFailure",
                info = new string('x', 51),
                timestamp = fixedTime,
            }
        );
        Assert.Equal(4, oversizedInfo[0].GetInt32());
        Assert.Null((await ChargingViewAsync(driver)).Session!.FaultCode);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task ClearingStationFaultRestoresEachConnectorWithoutErasingItsOwnFault()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        Guid secondConnectorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = await db.OcppChargePoints.SingleAsync(
                x => x.Identity == "SIM-001",
                timeout.Token
            );
            var first = await db.OcppConnectors.SingleAsync(
                x => x.ChargePointId == device.Id,
                timeout.Token
            );
            var second = new ChargingConnector
            {
                StationId = first.StationId!.Value,
                Number = 2,
                IsOnline = true,
            };
            secondConnectorId = second.Id;
            db.ChargingConnectors.Add(second);
            db.OcppConnectors.Add(
                new OcppConnector
                {
                    ChargePointId = device.Id,
                    ConnectorNumber = 2,
                    StationId = first.StationId,
                    BusinessConnectorId = second.Id,
                }
            );
            await db.SaveChangesAsync(timeout.Token);
        }
        var fixedTime = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, fixedTime);
        await StatusAsync(socket, "Charging", fixedTime);
        await SimulatorMeterAsync(
            socket,
            fixedTime,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ]
        );
        await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 2,
                status = "Available",
                errorCode = "ConnectorLockFailure",
                info = "Second connector locked",
                timestamp = fixedTime,
            }
        );
        await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 0,
                status = "Faulted",
                errorCode = "GroundFailure",
                info = "Station earth fault",
                timestamp = fixedTime,
            }
        );
        var faulted = await ChargingViewAsync(driver);
        Assert.Equal("Charging", faulted.Session!.Status);
        Assert.Null(faulted.Session.FaultCode);
        Assert.Equal(22m, faulted.Session.PowerKW);
        Assert.Equal("Faulted", faulted.Connector!.DeviceStatus);
        Assert.Equal("GroundFailure", faulted.Connector.DeviceFaultCode);
        Assert.Equal("Station earth fault", faulted.Connector.DeviceFaultInfo);
        await StatusAsync(socket, "Charging", fixedTime);
        Assert.Equal("GroundFailure", (await ChargingViewAsync(driver)).Connector!.DeviceFaultCode);

        await StatusAsync(socket, "Available", fixedTime, connector: 0);
        var clearStation = await ChargingViewAsync(driver);
        Assert.Equal("Charging", clearStation.Session!.Status);
        Assert.Null(clearStation.Session.FaultCode);
        Assert.Null(clearStation.Connector!.DeviceFaultCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var second = await db.ChargingConnectors.SingleAsync(
                x => x.Id == secondConnectorId,
                timeout.Token
            );
            Assert.Equal(ChargingState.Available, second.Status);
            Assert.Equal("ConnectorLockFailure", second.FaultCode);
            Assert.Equal("Second connector locked", second.FaultInfo);
        }
        await StatusAsync(socket, "Available", fixedTime, connector: 2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var second = await db.ChargingConnectors.SingleAsync(
                x => x.Id == secondConnectorId,
                timeout.Token
            );
            Assert.Equal(ChargingState.Available, second.Status);
            Assert.Null(second.FaultCode);
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task ManualDemoPowerAndSensorsUseReceiptOrderWithTimestampBeforeRefreshedStart()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var fixedTime = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, fixedTime.AddMinutes(1));
        await StatusAsync(socket, "Charging", fixedTime.AddMinutes(2));
        await SimulatorMeterAsync(
            socket,
            fixedTime,
            [
                new
                {
                    value = "21500",
                    measurand = "Power.Active.Import",
                    unit = "W",
                    phase = "L1",
                },
                new
                {
                    value = "45",
                    measurand = "SoC",
                    unit = "Percent",
                    phase = "L1",
                },
                new
                {
                    value = "86",
                    measurand = "Temperature",
                    unit = "Fahrenheit",
                    phase = "L1",
                },
            ]
        );
        var first = (await ChargingViewAsync(driver)).Session!;
        Assert.Equal(21.5m, first.PowerKW);
        Assert.Equal(45m, first.StateOfCharge);
        Assert.Equal(30m, first.TemperatureCelsius);
        await StatusAsync(socket, "SuspendedEV", fixedTime);
        Assert.Null((await ChargingViewAsync(driver)).Session!.PowerKW);
        await StatusAsync(socket, "Charging", fixedTime);
        Assert.Null((await ChargingViewAsync(driver)).Session!.PowerKW);
        await SimulatorMeterAsync(
            socket,
            fixedTime,
            [
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                    phase = "L1",
                },
            ]
        );
        Assert.Equal(22m, (await ChargingViewAsync(driver)).Session!.PowerKW);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task MeterDropdownMetadataRemainsVisibleWithoutCorruptingChargingTotals()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, observed);
        await StatusAsync(socket, "Charging", observed);
        await SimulatorMeterAsync(
            socket,
            observed,
            [
                new { value = "2500", unit = "Wh" },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ]
        );
        var contexts = new[]
        {
            "Interruption.Begin",
            "Interruption.End",
            "Other",
            "Sample.Clock",
            "Sample.Periodic",
            "Transaction.Begin",
            "Transaction.End",
            "Trigger",
        };
        foreach (var context in contexts)
            await SimulatorMeterAsync(
                socket,
                observed,
                [
                    new
                    {
                        value = "230",
                        measurand = "Voltage",
                        unit = "V",
                        phase = "L1-N",
                        location = "Cable",
                        context,
                        format = "Raw",
                    },
                ]
            );
        await SimulatorMeterAsync(
            socket,
            observed,
            [
                new
                {
                    value = "signed-meter-observation",
                    measurand = "Energy.Active.Import.Register",
                    unit = "Wh",
                    phase = "L1",
                    location = "Outlet",
                    context = "Other",
                    format = "SignedData",
                },
            ]
        );
        var state = await ChargingViewAsync(driver);
        Assert.Equal(2.5m, state.Session!.EnergyKWh);
        Assert.Equal(22m, state.Session.PowerKW);
        Assert.All(
            contexts,
            context =>
                Assert.Contains(
                    state.Diagnostics!.Samples,
                    x =>
                        x.Measurand == "Voltage"
                        && x.Context == context
                        && x.Phase == "L1-N"
                        && x.Location == "Cable"
                        && x.Unit == "V"
                        && x.Format == "Raw"
                )
        );
        Assert.Contains(
            state.Diagnostics!.Samples,
            x => x.Format == "SignedData" && x.Value == "signed-meter-observation"
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task ValidStopCompletesOnceAndLaterStatusOrMeterCannotReopenIt()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        var transactionId = await SimulatorStartAsync(socket, started);
        await StatusAsync(socket, "Charging", started);
        await SimulatorMeterAsync(
            socket,
            started.AddMinutes(5),
            [new { value = "2383", unit = "Wh" }],
            transactionId
        );
        var id = (await ChargingViewAsync(driver)).Session!.Id;
        var stop = new
        {
            transactionId,
            meterStop = 20000,
            timestamp = started.AddMinutes(55),
            reason = "EVDisconnected",
        };
        Assert.Equal(3, (await CallAsync(socket, "StopTransaction", stop))[0].GetInt32());
        Assert.Equal(3, (await CallAsync(socket, "StopTransaction", stop))[0].GetInt32());
        await StatusAsync(socket, "Charging", started);
        await SimulatorMeterAsync(
            socket,
            started.AddMinutes(56),
            [
                new { value = "21000", unit = "Wh" },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transactionId
        );
        var completed = await ChargingViewAsync(driver);
        Assert.Equal(id, completed.Session!.Id);
        Assert.Equal("Completed", completed.Session.Status);
        Assert.Equal(20m, completed.Session.EnergyKWh);
        Assert.Equal(0m, completed.Session.PowerKW);
        Assert.Equal(started.AddMinutes(55), completed.Session.StoppedAt);
        Assert.Equal("EVDisconnected", completed.Diagnostics!.StopReason);
        Assert.Equal(
            20m,
            Assert
                .Single(
                    (
                        await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                            "/api/v1/driver/history",
                            timeout.Token
                        )
                    )!
                )
                .EnergyKWh
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        Assert.Equal("Completed", (await ChargingViewAsync(driver)).Session!.Status);
    }

    [Fact]
    public async Task ContradictoryFinalRegisterClosesSessionWithoutCreatingAZeroBill()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        var transactionId = await SimulatorStartAsync(socket, started);
        await StatusAsync(socket, "Charging", started);
        await SimulatorMeterAsync(
            socket,
            started.AddMinutes(1),
            [new { value = "2383", unit = "Wh" }],
            transactionId
        );
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 0,
                timestamp = started.AddMinutes(2),
                reason = "Local",
            }
        );
        var completed = (await ChargingViewAsync(driver)).Session!;
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.StoppedAt);
        Assert.Null(completed.EnergyKWh);
        Assert.Null(completed.Cost);
        Assert.Equal("FinalMeterRegression", completed.CompletionWarning);
        Assert.Empty(
            (
                await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!
        );
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var transaction = await db.OcppChargingTransactions.SingleAsync(timeout.Token);
            Assert.Equal(0, transaction.MeterStopWh);
            Assert.NotNull(transaction.StopMessageId);
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task RealChargerRetainsTimestampOrderingAcrossPauseAndResume()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var station = await db.Stations.SingleAsync(x => x.Code == "SIM-001", timeout.Token);
            station.IsDemo = false;
            await db.SaveChangesAsync(timeout.Token);
        }
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);
        var transactionId = await SimulatorStartAsync(socket, started);
        await StatusAsync(socket, "Charging", started);
        await SimulatorMeterAsync(
            socket,
            started.AddSeconds(10),
            [
                new { value = "1000", unit = "Wh" },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transactionId
        );
        Assert.Equal(22m, (await ChargingViewAsync(driver)).Session!.PowerKW);
        await StatusAsync(socket, "SuspendedEV", started.AddSeconds(20));
        await StatusAsync(socket, "Charging", started.AddSeconds(30));
        await SimulatorMeterAsync(
            socket,
            started.AddSeconds(25),
            [
                new
                {
                    value = "21",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transactionId
        );
        Assert.Null((await ChargingViewAsync(driver)).Session!.PowerKW);
        await SimulatorMeterAsync(
            socket,
            started.AddSeconds(40),
            [
                new
                {
                    value = "20",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            transactionId
        );
        Assert.Equal(20m, (await ChargingViewAsync(driver)).Session!.PowerKW);
        await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Charging",
                errorCode = "GroundFailure",
                timestamp = started.AddSeconds(29),
            }
        );
        var state = await ChargingViewAsync(driver);
        Assert.Equal("Charging", state.Session!.Status);
        Assert.Null(state.Session.FaultCode);
        Assert.Equal(20m, state.Session.PowerKW);
        Assert.Equal(transactionId.ToString(), state.Diagnostics!.ExternalSessionId);
        await SimulatorMeterAsync(
            socket,
            started.AddSeconds(-1),
            [new { value = "999999", unit = "Wh" }],
            transactionId
        );
        Assert.Equal(1m, (await ChargingViewAsync(driver)).Session!.EnergyKWh);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task ReusedTimestampFaultChangesArriveInPrivateStreamWithoutARefresh()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var fixedTime = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, fixedTime);
        await StatusAsync(socket, "Charging", fixedTime);
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

        async Task<DriverChargingSession> ReadFaultAsync(string? expected, string expectedStatus)
        {
            while (true)
            {
                if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                    continue;
                var line = await reader.ReadLineAsync(timeout.Token);
                Assert.StartsWith("data: ", line);
                using var json = JsonDocument.Parse(line![6..]);
                Assert.False(json.RootElement.TryGetProperty("recipientAccountId", out _));
                if (json.RootElement.GetProperty("session").ValueKind != JsonValueKind.Object)
                    continue;
                var session = json
                    .RootElement.GetProperty("session")
                    .Deserialize<DriverChargingSession>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    )!;
                if (session.FaultCode == expected && session.Status == expectedStatus)
                    return session;
            }
        }

        var versions = new List<long>();
        foreach (
            var (status, errorCode) in new[]
            {
                ("Charging", "GroundFailure"),
                ("Charging", "HighTemperature"),
                ("Faulted", "GroundFailure"),
                ("Charging", "NoError"),
            }
        )
        {
            await CallAsync(
                socket,
                "StatusNotification",
                new
                {
                    connectorId = 1,
                    status,
                    errorCode,
                    timestamp = fixedTime,
                }
            );
            var delivered = await ReadFaultAsync(errorCode == "NoError" ? null : errorCode, status);
            versions.Add(delivered.Version);
        }
        Assert.True(versions[0] < versions[1]);
        Assert.True(versions[1] < versions[2]);
        Assert.True(versions[2] < versions[3]);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task WaitingDriverReceivesPrivateAuthorizeHeartbeatAndUnboundMeterActivity()
    {
        using var driver = await DriverAsync();
        using var other = await DriverAsync("device-activity-observer@test.com");
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
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

        async Task<ChargingCommandResult> ReadCommandAsync(string action)
        {
            while (true)
            {
                if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                    continue;
                var line = await reader.ReadLineAsync(timeout.Token);
                Assert.StartsWith("data: ", line);
                using var json = JsonDocument.Parse(line![6..]);
                Assert.False(json.RootElement.TryGetProperty("recipientAccountId", out _));
                Assert.DoesNotContain("TEST1234", line);
                if (json.RootElement.GetProperty("eventType").GetString() != "DeviceCommandChanged")
                    continue;
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("session").ValueKind);
                var command = json
                    .RootElement.GetProperty("command")
                    .Deserialize<ChargingCommandResult>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    )!;
                if (command.Action == action)
                    return command;
            }
        }

        var authorizeResponse = await CallAsync(socket, "Authorize", new { idTag = "TEST1234" });
        Assert.Equal(
            "Invalid",
            authorizeResponse[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var authorize = await ReadCommandAsync("Authorize");
        Assert.False(authorize.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(authorize.FailureCode));
        Assert.True(authorize.Version > 0);
        await CallAsync(socket, "Heartbeat", new { });
        var heartbeat = await ReadCommandAsync("Heartbeat");
        Assert.True(heartbeat.Accepted);
        Assert.True(heartbeat.Version > authorize.Version);
        var fixedTime = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorMeterAsync(
            socket,
            fixedTime,
            [
                new
                {
                    value = "230",
                    measurand = "Voltage",
                    unit = "V",
                    phase = "L1-N",
                    location = "Inlet",
                    context = "Trigger",
                    format = "Raw",
                },
            ]
        );
        var meter = await ReadCommandAsync("MeterValues");
        Assert.True(meter.Accepted);
        Assert.True(meter.Version > heartbeat.Version);
        var delivered = Assert.Single(meter.Readings!);
        Assert.Equal("Voltage", delivered.Measurand);
        Assert.Equal("230", delivered.Value);
        Assert.Equal("L1-N", delivered.Phase);
        Assert.Equal("Inlet", delivered.Location);
        var waiting = await ChargingViewAsync(driver);
        Assert.Null(waiting.Session);
        Assert.Contains(
            waiting.Diagnostics!.Samples,
            x => x.Measurand == "Voltage" && x.Value == "230"
        );
        Assert.Null((await ChargingViewAsync(other)).Diagnostics);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var commands = await db
                .RealtimeOutbox.Where(x => x.EventType == "DeviceCommandChanged")
                .ToListAsync(timeout.Token);
            Assert.True(commands.Count >= 3);
            Assert.All(
                commands,
                x =>
                {
                    Assert.Equal("Driver", x.Audience);
                    Assert.NotNull(x.RecipientAccountId);
                }
            );
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task RoundedTemperatureOutsideDatabasePrecisionIsRetainedOnlyAsRawTelemetry()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, observed);
        await StatusAsync(socket, "Charging", observed);
        await SimulatorMeterAsync(
            socket,
            observed,
            [
                new
                {
                    value = "99999.9996",
                    measurand = "Temperature",
                    unit = "Celsius",
                    phase = "L1",
                },
            ]
        );
        var state = await ChargingViewAsync(driver);
        Assert.Null(state.Session!.TemperatureCelsius);
        Assert.Contains(
            state.Diagnostics!.Samples,
            x => x.Measurand == "Temperature" && x.Value == "99999.9996"
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task LatePreviousTransactionReadingsArePrivateAfterTheNextDriverConnects()
    {
        using var firstDriver = await DriverAsync();
        using var nextDriver = await DriverAsync("next-session-owner@test.com");
        using var socket = await ConnectAsync();
        await ClaimAsync(firstDriver);
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        var previousTransactionId = await SimulatorStartAsync(socket, started);
        await StatusAsync(socket, "Charging", started);
        await SimulatorMeterAsync(
            socket,
            started,
            [new { value = "1111", unit = "Wh" }],
            previousTransactionId
        );
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = previousTransactionId,
                meterStop = 2222,
                timestamp = started.AddMinutes(1),
            }
        );
        await ClaimAsync(nextDriver);
        var nextTransactionId = await SimulatorStartAsync(
            socket,
            started.AddMinutes(2),
            meterStart: 2222
        );
        Assert.NotEqual(previousTransactionId, nextTransactionId);
        await StatusAsync(socket, "Charging", started.AddMinutes(2));
        await SimulatorMeterAsync(
            socket,
            started.AddMinutes(3),
            [new { value = "59999", unit = "Wh" }],
            previousTransactionId
        );
        var next = await ChargingViewAsync(nextDriver);
        Assert.Equal(0m, next.Session!.EnergyKWh);
        Assert.DoesNotContain(next.Session.Readings ?? [], x => x.Value == "59999");
        Assert.DoesNotContain(next.Diagnostics!.Samples, x => x.Value == "59999");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var nextDriverId = (
                await db.Accounts.SingleAsync(
                    x => x.Email == "next-session-owner@test.com",
                    timeout.Token
                )
            ).Id;
            var visible = await db
                .RealtimeOutbox.Where(x =>
                    x.RecipientAccountId == nextDriverId || x.Audience == "PublicStation"
                )
                .ToListAsync(timeout.Token);
            Assert.All(visible, x => Assert.DoesNotContain("59999", x.PayloadJson));
            Assert.Contains(
                await db.OcppMeterSamples.ToListAsync(timeout.Token),
                x => x.ValueText == "59999"
            );
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task CompleteOcppMeterEnumMatrixIsRetainedAndDeliveredInTheHttpStream()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        await ClaimAsync(driver);
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        await SimulatorStartAsync(socket, observed);
        await StatusAsync(socket, "Charging", observed);
        await SimulatorMeterAsync(
            socket,
            observed,
            [
                new { value = "1000", unit = "Wh" },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ]
        );
        var measurands = new[]
        {
            "Current.Export",
            "Current.Import",
            "Current.Offered",
            "Energy.Active.Export.Register",
            "Energy.Active.Import.Register",
            "Energy.Reactive.Export.Register",
            "Energy.Reactive.Import.Register",
            "Energy.Active.Export.Interval",
            "Energy.Active.Import.Interval",
            "Energy.Reactive.Export.Interval",
            "Energy.Reactive.Import.Interval",
            "Frequency",
            "Power.Active.Export",
            "Power.Active.Import",
            "Power.Factor",
            "Power.Offered",
            "Power.Reactive.Export",
            "Power.Reactive.Import",
            "RPM",
            "SoC",
            "Temperature",
            "Voltage",
        };
        var units = new[]
        {
            "Wh",
            "kWh",
            "varh",
            "kvarh",
            "W",
            "kW",
            "VA",
            "kVA",
            "var",
            "kvar",
            "A",
            "V",
            "Celsius",
            "Fahrenheit",
            "K",
            "Percent",
        };
        var phases = new[]
        {
            "L1",
            "L2",
            "L3",
            "N",
            "L1-N",
            "L2-N",
            "L3-N",
            "L1-L2",
            "L2-L3",
            "L3-L1",
        };
        var locations = new[] { "Body", "Cable", "EV", "Inlet", "Outlet" };
        var contexts = new[]
        {
            "Interruption.Begin",
            "Interruption.End",
            "Other",
            "Sample.Clock",
            "Sample.Periodic",
            "Transaction.Begin",
            "Transaction.End",
            "Trigger",
        };
        var samples = new List<object>();
        samples.AddRange(
            measurands.Select(measurand => new
            {
                value = "7.25",
                measurand,
                unit = "Wh",
                format = "SignedData",
            })
        );
        samples.AddRange(
            units.Select(unit => new
            {
                value = "7.25",
                measurand = "Current.Offered",
                unit,
                format = "SignedData",
            })
        );
        samples.AddRange(
            phases.Select(phase => new
            {
                value = "7.25",
                measurand = "Current.Offered",
                phase,
                format = "SignedData",
            })
        );
        samples.AddRange(
            locations.Select(location => new
            {
                value = "7.25",
                measurand = "Current.Offered",
                location,
                format = "SignedData",
            })
        );
        samples.AddRange(
            contexts.Select(context => new
            {
                value = "7.25",
                measurand = "Current.Offered",
                context,
                format = "SignedData",
            })
        );
        samples.AddRange(
            new[] { "Raw", "SignedData" }.Select(format => new
            {
                value = "7.25",
                measurand = "Current.Offered",
                format,
            })
        );
        samples.Add(
            new
            {
                value = "999999999999.999999999",
                measurand = "RPM",
                format = "Raw",
            }
        );
        Assert.Equal(64, samples.Count);
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
        await SimulatorMeterAsync(socket, observed, samples.ToArray());
        ChargingCommandResult? delivered = null;
        while (delivered is null)
        {
            if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                continue;
            var line = await reader.ReadLineAsync(timeout.Token);
            Assert.StartsWith("data: ", line);
            using var json = JsonDocument.Parse(line![6..]);
            if (json.RootElement.GetProperty("eventType").GetString() != "DeviceCommandChanged")
                continue;
            var command = json
                .RootElement.GetProperty("command")
                .Deserialize<ChargingCommandResult>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                )!;
            if (command.Action == "MeterValues" && command.Readings?.Count == 64)
            {
                Assert.Equal(
                    "Charging",
                    json.RootElement.GetProperty("connector").GetProperty("status").GetString()
                );
                delivered = command;
            }
        }
        Assert.All(
            measurands,
            value => Assert.Contains(delivered.Readings!, x => x.Measurand == value)
        );
        Assert.All(units, value => Assert.Contains(delivered.Readings!, x => x.Unit == value));
        Assert.All(phases, value => Assert.Contains(delivered.Readings!, x => x.Phase == value));
        Assert.All(
            locations,
            value => Assert.Contains(delivered.Readings!, x => x.Location == value)
        );
        Assert.All(
            contexts,
            value => Assert.Contains(delivered.Readings!, x => x.Context == value)
        );
        var state = await ChargingViewAsync(driver);
        Assert.Equal(1m, state.Session!.EnergyKWh);
        Assert.Equal(22m, state.Session.PowerKW);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var boundary = await db.OcppMeterSamples.SingleAsync(
                x => x.ValueText == "999999999999.999999999",
                timeout.Token
            );
            Assert.Null(boundary.NumericValue);
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task EveryStandardStopReasonCompletesAndRemainsAvailableInPrivateHistory()
    {
        using var driver = await DriverAsync();
        using var socket = await ConnectAsync();
        var reasons = new[]
        {
            "EmergencyStop",
            "EVDisconnected",
            "HardReset",
            "Local",
            "Other",
            "PowerLoss",
            "Reboot",
            "Remote",
            "SoftReset",
            "UnlockCommand",
            "DeAuthorized",
        };
        var started = DateTimeOffset.UtcNow.AddMinutes(-20);
        foreach (var reason in reasons)
        {
            await ClaimAsync(driver);
            var transactionId = await SimulatorStartAsync(socket, started);
            var stopped = await CallAsync(
                socket,
                "StopTransaction",
                new
                {
                    transactionId,
                    meterStop = 100,
                    timestamp = started.AddMinutes(1),
                    reason,
                }
            );
            Assert.Equal(3, stopped[0].GetInt32());
            var session = (await ChargingViewAsync(driver)).Session!;
            Assert.Equal("Completed", session.Status);
            Assert.Equal(reason, session.StopReason);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
                var owner = await db.Accounts.SingleAsync(
                    x => x.Email == "driver@test.com",
                    timeout.Token
                );
                var transaction = await db.OcppChargingTransactions.SingleAsync(
                    x => x.Ocpp16TransactionId == transactionId,
                    timeout.Token
                );
                var inbound = await db.OcppInboundMessages.SingleAsync(
                    x => x.Id == transaction.StopMessageId,
                    timeout.Token
                );
                Assert.Contains(
                    await db.RealtimeOutbox.ToListAsync(timeout.Token),
                    x =>
                        x.EventType == "DeviceCommandChanged"
                        && x.Audience == "Driver"
                        && x.RecipientAccountId == owner.Id
                        && JsonSerializer.Deserialize<StationChanged>(x.PayloadJson)?.Command
                            is { Action: "StopTransaction", Accepted: true } command
                        && command.Version == inbound.CommandVersion
                );
            }
            started = started.AddMinutes(1);
        }
        Assert.Equal(
            reasons.Length,
            (
                await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                    "/api/v1/driver/history",
                    timeout.Token
                )
            )!.Length
        );
        await ClaimAsync(driver);
        var finalTransaction = await SimulatorStartAsync(socket, started);
        var invalidReason = await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = finalTransaction,
                meterStop = 100,
                timestamp = started.AddMinutes(1),
                reason = "ImaginaryReason",
            }
        );
        Assert.Equal(4, invalidReason[0].GetInt32());
        Assert.Equal("PropertyConstraintViolation", invalidReason[2].GetString());
        Assert.Null((await ChargingViewAsync(driver)).Session!.StoppedAt);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task TenSimulatorMapSeparatesTheSevenOtherDriversFromTheOwnThreePoints()
    {
        using var driver = await DriverAsync();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        configuration["Demo:PrepareOtherDrivers"] = "true";
        configuration["Demo:OtherDriverPassword"] = "demo-other-driver-test-passphrase";
        await EVCharging.Api.DemoSimulatorScenario.PrepareAsync(
            factory.Services,
            configuration,
            timeout.Token
        );
        var stations = (
            await driver.GetFromJsonAsync<DriverStationDto[]>(
                "/api/v1/driver/stations",
                timeout.Token
            )
        )!;
        Assert.Equal(
            Enumerable.Range(1, 10).Select(x => $"SIM-{x:000}"),
            stations
                .Where(x => x.Code.StartsWith("SIM-", StringComparison.Ordinal))
                .Select(x => x.Code)
                .OrderBy(x => x)
        );

        using var otherDriver = factory.CreateClient();
        var login = await otherDriver.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = EVCharging.Api.DemoSimulatorScenario.OtherDriverEmail(4),
                password = "demo-other-driver-test-passphrase",
            },
            timeout.Token
        );
        login.EnsureSuccessStatusCode();
        var authentication = (await login.Content.ReadFromJsonAsync<AuthResult>(timeout.Token))!;
        otherDriver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            authentication.AccessToken
        );
        using var ownSocket = await ConnectAsync();
        using var otherSocket = await ConnectAsync("SIM-004");
        await ClaimAsync(driver);
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        var ownTransaction = await SimulatorStartAsync(ownSocket, started);
        await StatusAsync(ownSocket, "Charging", started);
        await SimulatorMeterAsync(
            ownSocket,
            started,
            [
                new { value = "1500", unit = "Wh" },
                new
                {
                    value = "22",
                    measurand = "Power.Active.Import",
                    unit = "kW",
                },
            ],
            ownTransaction
        );
        var ownSession = (await ChargingViewAsync(driver)).Session!;
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
        var otherTransaction = await SimulatorStartAsync(otherSocket, started);
        await StatusAsync(otherSocket, "Charging", started);
        await SimulatorMeterAsync(
            otherSocket,
            started,
            [new { value = "9000", unit = "Wh" }],
            otherTransaction
        );
        await CallAsync(
            otherSocket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Faulted",
                errorCode = "GroundFailure",
                timestamp = started,
            }
        );
        var otherStationId = Assert.Single(stations, x => x.Code == "SIM-004").Id;
        while (true)
        {
            if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                continue;
            var line = await reader.ReadLineAsync(timeout.Token);
            Assert.StartsWith("data: ", line);
            using var json = JsonDocument.Parse(line![6..]);
            if (json.RootElement.GetProperty("stationId").GetGuid() != otherStationId)
                continue;
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("session").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("command").ValueKind);
            var publicStation = json.RootElement.GetProperty("station");
            if (
                publicStation.ValueKind == JsonValueKind.Object
                && publicStation.GetProperty("status").GetString() == "OutOfService"
            )
                break;
        }
        var visibleOther = await ChargingViewAsync(driver, "SIM-004");
        Assert.True(visibleOther.InUse);
        Assert.Null(visibleOther.Session);
        Assert.Null(visibleOther.Diagnostics);
        var ownerOther = await ChargingViewAsync(otherDriver, "SIM-004");
        Assert.Equal(9m, ownerOther.Session!.EnergyKWh);
        Assert.Equal("Faulted", ownerOther.Session.Status);
        Assert.Equal("GroundFailure", ownerOther.Session.FaultCode);
        Assert.Contains(ownerOther.Diagnostics!.Samples, x => x.Value == "9000");
        var priorMapVersion = visibleOther.Station.Version;
        await CallAsync(
            otherSocket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Charging",
                errorCode = "HighTemperature",
                timestamp = started,
            }
        );
        while (true)
        {
            if (await reader.ReadLineAsync(timeout.Token) != "event: changed")
                continue;
            var line = await reader.ReadLineAsync(timeout.Token);
            Assert.StartsWith("data: ", line);
            using var json = JsonDocument.Parse(line![6..]);
            if (json.RootElement.GetProperty("stationId").GetGuid() != otherStationId)
                continue;
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("session").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("command").ValueKind);
            var publicStation = json.RootElement.GetProperty("station");
            if (
                publicStation.ValueKind == JsonValueKind.Object
                && publicStation.GetProperty("status").GetString() == "Charging"
                && publicStation.GetProperty("warningCode").GetString() == "HighTemperature"
            )
            {
                Assert.True(publicStation.GetProperty("version").GetInt64() > priorMapVersion);
                break;
            }
        }
        var ownAfterOther = (await ChargingViewAsync(driver)).Session!;
        Assert.Equal(ownSession.Id, ownAfterOther.Id);
        Assert.Equal(ownSession.Version, ownAfterOther.Version);
        Assert.Equal("Charging", ownAfterOther.Status);
        Assert.Equal(1.5m, ownAfterOther.EnergyKWh);
        Assert.Equal(22m, ownAfterOther.PowerKW);
        Assert.Null((await ChargingViewAsync(driver)).Station.WarningCode);

        foreach (var identity in new[] { "SIM-002", "SIM-003" })
        {
            using var socket = await ConnectAsync(identity);
            await StatusAsync(socket, "Available", started);
            var ownPoint = await ChargingViewAsync(driver, identity);
            Assert.True(ownPoint.DeviceConnected);
            Assert.True(ownPoint.CanConnect);
            Assert.False(ownPoint.InUse);
            Assert.Null(ownPoint.Session);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal(
                7,
                await db.Accounts.CountAsync(
                    x => x.Email.StartsWith("simulator-") && x.Email.EndsWith("@example.test"),
                    timeout.Token
                )
            );
            Assert.Equal(
                8,
                await db.ChargingConnectionRequests.CountAsync(
                    x => x.State == "Waiting" || x.State == "Linked",
                    timeout.Token
                )
            );
        }
        await ownSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await otherSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}

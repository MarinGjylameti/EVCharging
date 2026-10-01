using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OcppWebSocketTests : IDisposable
{
    private readonly ApiFactory factory = new(simulator: true);
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

    private async Task<HttpClient> DriverAsync(string email = "driver@test.com")
    {
        var client = factory.CreateClient();
        factory.Services.GetRequiredService<IConfiguration>()["Demo:DriverEmail"] =
            "driver@test.com";
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email,
                password = "driver-test-passphrase",
                firstName = "Test",
                lastName = "Driver",
                language = "sq",
            },
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>(timeout.Token))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        return client;
    }

    private async Task<WebSocket> ConnectAsync(
        string identity = "SIM-001",
        string password = "simulator-test-passphrase"
    )
    {
        _ = factory.CreateClient();
        var client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("ocpp1.6");
        client.ConfigureRequest = request =>
            request.Headers.Authorization =
                "Basic "
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(identity + ":" + password));
        return await client.ConnectAsync(
            new Uri("ws://localhost/ws/cs/" + identity),
            timeout.Token
        );
    }

    private async Task<JsonElement> CallAsync(
        WebSocket socket,
        string action,
        object payload,
        string? id = null
    )
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new object[] { 2, id ?? Guid.NewGuid().ToString(), action, payload }
        );
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[16_384];
        using var reply = new MemoryStream();
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            reply.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        using var document = JsonDocument.Parse(reply.ToArray());
        return document.RootElement.Clone();
    }

    private Task<JsonElement> StatusAsync(
        WebSocket socket,
        string status,
        DateTimeOffset? at = null,
        int connector = 1
    ) =>
        CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = connector,
                errorCode = "NoError",
                status,
                timestamp = at ?? DateTimeOffset.UtcNow,
            }
        );

    [Fact]
    public async Task CompleteChargingCycleUpdatesPrivateSessionAndHistoryWithoutMarkingFreeEarly()
    {
        using var driver = await DriverAsync();
        using var otherDriver = await DriverAsync("other@test.com");
        using var socket = await ConnectAsync();
        var boot = await CallAsync(
            socket,
            "BootNotification",
            new
            {
                chargePointVendor = "BW Charging",
                chargePointModel = "Simulator16",
                chargePointSerialNumber = "SIM-001",
            }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        Assert.Equal(
            "Accepted",
            (await CallAsync(socket, "Authorize", new { idTag = "TAG-001" }))[2]
                .GetProperty("idTagInfo")
                .GetProperty("status")
                .GetString()
        );
        await StatusAsync(socket, "Available");
        var started = DateTimeOffset.UtcNow;
        var startPayload = new
        {
            connectorId = 1,
            idTag = "TAG-001",
            meterStart = 1000,
            timestamp = started,
        };
        var start = await CallAsync(socket, "StartTransaction", startPayload, "start-1");
        Assert.Equal(3, start[0].GetInt32());
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        Assert.True(transactionId > 0);
        Assert.Equal(
            start.ToString(),
            (await CallAsync(socket, "StartTransaction", startPayload, "start-1")).ToString()
        );
        await StatusAsync(socket, "Charging");
        var meter = new
        {
            connectorId = 1,
            transactionId,
            meterValue = new[]
            {
                new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    sampledValue = new[]
                    {
                        new
                        {
                            value = "2500",
                            measurand = "Energy.Active.Import.Register",
                            unit = "Wh",
                        },
                        new
                        {
                            value = "22",
                            measurand = "Power.Active.Import",
                            unit = "kW",
                        },
                        new
                        {
                            value = "45",
                            measurand = "SoC",
                            unit = "Percent",
                        },
                    },
                },
            },
        };
        Assert.Equal(3, (await CallAsync(socket, "MeterValues", meter, "meter-1"))[0].GetInt32());
        await CallAsync(socket, "MeterValues", meter, "meter-1");
        var state = (
            await driver.GetFromJsonAsync<DriverChargingState>(
                "/api/v1/driver/charging-sessions",
                timeout.Token
            )
        )!;
        var session = Assert.Single(state.Sessions);
        Assert.True(state.CanTestSimulator);
        Assert.Equal("Charging", session.Status);
        Assert.Equal(1.5m, session.EnergyKWh);
        Assert.Equal(22m, session.PowerKW);
        Assert.Equal(45m, session.StateOfCharge);
        Assert.Empty(
            (
                await otherDriver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
        await StatusAsync(socket, "Available");
        Assert.Equal("Occupied", await StationStatusAsync(driver, "SIM-001"));
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 3500,
                timestamp = DateTimeOffset.UtcNow,
                reason = "Local",
            },
            "stop-1"
        );
        Assert.Equal("Occupied", await StationStatusAsync(driver, "SIM-001"));
        await StatusAsync(socket, "Available");
        Assert.Equal("Available", await StationStatusAsync(driver, "SIM-001"));
        var history = (
            await driver.GetFromJsonAsync<DriverHistoryDto[]>(
                "/api/v1/driver/history",
                timeout.Token
            )
        )!;
        Assert.Equal(2.5m, Assert.Single(history).EnergyKWh);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.Single(await db.OcppChargingTransactions.ToListAsync(timeout.Token));
        Assert.Equal(3, await db.OcppMeterSamples.CountAsync(timeout.Token));
        Assert.Contains(
            await db.RealtimeOutbox.ToListAsync(timeout.Token),
            entry => entry.Audience == "Driver"
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task MultipleSimulatorsKeepIndependentStatesAndIgnoreOldStatus()
    {
        using var driver = await DriverAsync();
        using var first = await ConnectAsync();
        using var second = await ConnectAsync("SIM-002");
        using var third = await ConnectAsync("SIM-003");
        var startPayload = new
        {
            connectorId = 1,
            idTag = "TAG-001",
            meterStart = 0,
            timestamp = DateTimeOffset.UtcNow,
        };
        Assert.Equal(3, (await CallAsync(first, "StartTransaction", startPayload))[0].GetInt32());
        Assert.Equal(3, (await CallAsync(second, "StartTransaction", startPayload))[0].GetInt32());
        await StatusAsync(first, "Charging");
        await StatusAsync(second, "Preparing");
        await StatusAsync(third, "Faulted");
        await StatusAsync(first, "Available", DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.Equal("Charging", await StationStatusAsync(driver, "SIM-001"));
        Assert.Equal("Occupied", await StationStatusAsync(driver, "SIM-002"));
        Assert.Equal("OutOfService", await StationStatusAsync(driver, "SIM-003"));
        Assert.Equal(
            2,
            (
                await driver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!
                .Sessions
                .Count
        );
        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await second.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await third.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task CommittedWebSocketStatusReachesAuthenticatedEventStream()
    {
        using var driver = await DriverAsync();
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
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(timeout.Token)
        );
        string? line;
        do
        {
            line = await reader.ReadLineAsync(timeout.Token);
        } while (line != "event: ready");
        using var socket = await ConnectAsync();
        await StatusAsync(socket, "Available");
        do
        {
            line = await reader.ReadLineAsync(timeout.Token);
        } while (line != "event: changed");
        Assert.Equal("Available", await StationStatusAsync(driver, "SIM-001"));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task ExplicitAnonymousDemoAcceptsOnlyConfiguredSimulatorIdentities()
    {
        using var demoFactory = new ApiFactory(simulator: true, anonymousSimulators: true);
        _ = demoFactory.CreateClient();
        var client = demoFactory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("ocpp1.6");
        using var socket = await client.ConnectAsync(
            new Uri("ws://localhost/ws/cs/SIM-001"),
            timeout.Token
        );
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(new Uri("ws://localhost/ws/cs/REAL-DEVICE"), timeout.Token)
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task RejectsWrongCredentialsUnknownConnectorAndFractionalWh()
    {
        using var driver = await DriverAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConnectAsync(password: "incorrect-password")
        );
        using var socket = await ConnectAsync();
        Assert.Equal(4, (await StatusAsync(socket, "Available", connector: 9))[0].GetInt32());
        var reply = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "TAG-001",
                meterStart = 1.5,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(4, reply[0].GetInt32());
        Assert.Equal("PropertyConstraintViolation", reply[2].GetString());
        Assert.Empty(
            (
                await driver.GetFromJsonAsync<DriverChargingState>(
                    "/api/v1/driver/charging-sessions",
                    timeout.Token
                )
            )!.Sessions
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    private async Task<string> StationStatusAsync(HttpClient driver, string identity)
    {
        var stations = (
            await driver.GetFromJsonAsync<DriverStationDto[]>(
                "/api/v1/driver/stations",
                timeout.Token
            )
        )!;
        return Assert.Single(stations, station => station.Code == identity).Status;
    }

    public void Dispose()
    {
        timeout.Dispose();
        factory.Dispose();
    }
}

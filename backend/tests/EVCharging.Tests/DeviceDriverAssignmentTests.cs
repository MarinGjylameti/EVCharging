using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    private async Task<DeviceDto> AssignAsync(
        HttpClient admin,
        Guid stationId,
        DeviceDto device,
        Guid? driverId,
        string? card
    )
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stations/{stationId}/device/driver",
            new DeviceDriverInput(device.Version, driverId, card),
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceDto>(timeout.Token))!;
    }

    private async Task<DriverStationCharging> ViewAsync(
        HttpClient driver,
        Guid stationId,
        int connector = 1
    ) =>
        (
            await driver.GetFromJsonAsync<DriverStationCharging>(
                $"/api/v1/driver/stations/{stationId}/charging?connector={connector}",
                timeout.Token
            )
        )!;

    private async Task<int> StartPhysicalAsync(
        WebSocket socket,
        int connector,
        string tag,
        DateTimeOffset started
    )
    {
        var result = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = connector,
                idTag = tag,
                meterStart = 1000,
                timestamp = started,
            }
        );
        Assert.Equal(3, result[0].GetInt32());
        return result[2].GetProperty("transactionId").GetInt32();
    }

    [Fact]
    public async Task DemoRecipientSeesIdleSignalsAndLateSessionSnapshotWhileOtherDriversRemainPrivate()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("demo@test.com", UserRole.Driver);
        var (other, otherId) = await UserAsync("other@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "PHYSICAL-DEMO");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        device = await AssignAsync(admin, station.Id, device, driverId, "  ");
        Assert.Null(device.CardIdTag);
        using var socket = await SocketAsync(device.Identity);
        await CallAsync(
            socket,
            "BootNotification",
            new { chargePointVendor = "Test", chargePointModel = "Demo" }
        );
        await CallAsync(socket, "Heartbeat", new { });
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
                        timestamp = DateTimeOffset.UtcNow,
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
        var idle = await ViewAsync(driver, station.Id);
        Assert.Equal("Demo", idle.DriverAccessMode);
        Assert.False(idle.CanConnect);
        Assert.Null(idle.Session);
        Assert.True(idle.DeviceConnected);
        Assert.Equal("MeterValues", idle.Diagnostics!.LastCommand!.Action);
        Assert.Contains(
            idle.Diagnostics.Samples,
            x => x.Measurand == "Voltage" && x.Value == "230"
        );
        Assert.Null((await ViewAsync(other, station.Id)).Diagnostics);
        var started = DateTimeOffset.UtcNow.AddMinutes(-15);
        var transaction = await StartPhysicalAsync(socket, 1, "ANY-DEMO-CARD", started);
        await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 1,
                status = "Charging",
                errorCode = "NoError",
                timestamp = DateTimeOffset.UtcNow,
            }
        );
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
                        timestamp = DateTimeOffset.UtcNow,
                        sampledValue = new[]
                        {
                            new
                            {
                                value = "3500",
                                measurand = "Energy.Active.Import.Register",
                                unit = "Wh",
                            },
                        },
                    },
                },
            }
        );
        using var reopened = factory.CreateClient();
        reopened.DefaultRequestHeaders.Authorization = driver.DefaultRequestHeaders.Authorization;
        var late = await ViewAsync(reopened, station.Id);
        Assert.Equal(started, late.Session!.StartedAt);
        Assert.Equal(2.5m, late.Session.EnergyKWh);
        Assert.Equal("Charging", late.Session.Status);
        Assert.False(late.InUse);
        Assert.Null((await ViewAsync(other, station.Id)).Session);
        Assert.Null((await ViewAsync(other, station.Id)).Diagnostics);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var owner = await db.ChargingConnectionRequests.SingleAsync();
            Assert.Equal(driverId, owner.DriverId);
            Assert.True(owner.IsAutomatic);
            Assert.Equal("Linked", owner.State);
            Assert.Contains(
                await db.RealtimeOutbox.ToListAsync(),
                x => x.RecipientAccountId == driverId && x.EventType == "DeviceCommandChanged"
            );
            Assert.DoesNotContain(
                await db.RealtimeOutbox.ToListAsync(),
                x => x.RecipientAccountId == otherId
            );
        }
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 5000,
                timestamp = DateTimeOffset.UtcNow,
                reason = "Local",
            }
        );
        var history = (
            await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history")
        )!;
        Assert.Equal(4m, Assert.Single(history).EnergyKWh);
        Assert.Equal(started, history[0].StartedAt);
        Assert.Empty((await other.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history"))!);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Theory]
    [InlineData("CARD-001")]
    [InlineData("{\"idTag\":\"CARD-001\"}")]
    [InlineData("[2,\"request-1\",\"Authorize\",{\"idTag\":\"CARD-001\"}]")]
    [InlineData(
        "[2,\"request-2\",\"StartTransaction\",{\"idTag\":\"CARD-001\",\"connectorId\":1}]"
    )]
    public async Task CardModeParsesCodeOrJsonAndAssignsOnlyMatchingCharges(string cardInput)
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("card@test.com", UserRole.Driver);
        var (other, _) = await UserAsync("other@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "CARD-POINT");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        device = await AssignAsync(admin, station.Id, device, driverId, cardInput);
        Assert.Equal("CARD-001", device.CardIdTag);
        using var socket = await SocketAsync(device.Identity);
        await CallAsync(socket, "Heartbeat", new { });
        Assert.Equal("Card", (await ViewAsync(driver, station.Id)).DriverAccessMode);
        Assert.Null((await ViewAsync(driver, station.Id)).Diagnostics);
        var denied = await CallAsync(socket, "Authorize", new { idTag = "DIFFERENT" });
        Assert.Equal(
            "Invalid",
            denied[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var badStart = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "DIFFERENT",
                meterStart = 0,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(4, badStart[0].GetInt32());
        Assert.Null((await ViewAsync(driver, station.Id)).Session);
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        var transaction = await StartPhysicalAsync(socket, 2, "CARD-001", started);
        var state = await ViewAsync(driver, station.Id, 2);
        Assert.Equal(started, state.Session!.StartedAt);
        Assert.Equal(2, state.Session.ConnectorId);
        Assert.Equal(2, state.Connector!.Number);
        Assert.NotNull(state.Diagnostics);
        Assert.Null((await ViewAsync(driver, station.Id, 1)).Session);
        Assert.Null((await ViewAsync(other, station.Id, 2)).Diagnostics);
        Assert.Null((await ViewAsync(other, station.Id, 2)).Session);
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 2000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task AnotherAllowedCardKeepsItsSessionPrivateWithoutUsingTheConfiguredDriverAsFallback()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("card@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "CARD-PRIVACY");
        var device = (await DeviceAsync(admin, station.Id)).Device; // CARD-001 is already allowed.
        await AssignAsync(admin, station.Id, device, driverId, "CARD-002");
        using var socket = await SocketAsync(device.Identity);
        var transaction = await StartPhysicalAsync(socket, 1, "CARD-001", DateTimeOffset.UtcNow);
        var view = await ViewAsync(driver, station.Id);
        Assert.True(view.InUse);
        Assert.Null(view.Session);
        Assert.Null(view.Diagnostics);
        var operations = (await admin.GetFromJsonAsync<SessionPage>("/api/v1/admin/sessions"))!;
        Assert.Null(Assert.Single(operations.Items).DriverId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.DoesNotContain(
                await db.RealtimeOutbox.ToListAsync(),
                x =>
                    x.RecipientAccountId == driverId
                    && x.EventType is "SessionStarted" or "DeviceCommandChanged"
            );
        }
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 2000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{}")]
    [InlineData("{\"idTag\":123}")]
    [InlineData("{\"idTag\":\"\"}")]
    [InlineData("{\"idTag\":\"A\",\"idTag\":\"B\"}")]
    [InlineData("[2,\"x\",\"MeterValues\",{\"idTag\":\"CARD\"}]")]
    [InlineData("123456789012345678901")]
    public async Task InvalidCardInputDoesNotSilentlyEnableDemo(string input)
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (_, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "CARD-POINT");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device/driver",
            new DeviceDriverInput(device.Version, driverId, input)
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var current = (
            await admin.GetFromJsonAsync<DeviceDto>($"/api/v1/admin/stations/{station.Id}/device")
        )!;
        Assert.Null(current.AssignedDriverId);
        Assert.Equal(device.Version, current.Version);
    }

    [Fact]
    public async Task DeviceAssignmentRequiresSuperadminAndActiveDriverAndKeepsAnActiveOwnerStable()
    {
        var (admin, adminId) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var (_, secondId) = await UserAsync("second@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "DEMO-REAL");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        var path = $"/api/v1/admin/stations/{station.Id}/device/driver";
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await driver.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version, driverId, null)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await admin.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version, adminId, null)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await admin.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version, null, "CARD")
                )
            ).StatusCode
        );
        device = await AssignAsync(admin, station.Id, device, driverId, null);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (
                await admin.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version - 1, secondId, null)
                )
            ).StatusCode
        );
        using var socket = await SocketAsync(device.Identity);
        var transaction = await StartPhysicalAsync(socket, 1, "ANY", DateTimeOffset.UtcNow);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await admin.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version, secondId, "CARD")
                )
            ).StatusCode
        );
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 2000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        device = await AssignAsync(admin, station.Id, device, secondId, "CARD");
        Assert.Single(
            (await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history"))!
        );
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal(driverId, (await db.ChargingConnectionRequests.SingleAsync()).DriverId);
            (await db.Accounts.SingleAsync(a => a.Id == driverId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await admin.PutAsJsonAsync(
                    path,
                    new DeviceDriverInput(device.Version, driverId, null)
                )
            ).StatusCode
        );
        device = await AssignAsync(admin, station.Id, device, null, null);
        Assert.Null(device.AssignedDriverId);
        Assert.Null(device.CardIdTag);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task AutomaticDemoAssignmentsAllowBothConnectorsAndKeepTheirMeasurementsSeparate()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("demo@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "MULTI-REAL");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        await AssignAsync(admin, station.Id, device, driverId, null);
        using var socket = await SocketAsync(device.Identity);
        var first = await StartPhysicalAsync(socket, 1, "ONE", DateTimeOffset.UtcNow);
        var second = await StartPhysicalAsync(socket, 2, "TWO", DateTimeOffset.UtcNow);
        var sessions = (
            await driver.GetFromJsonAsync<DriverChargingState>("/api/v1/driver/charging-sessions")
        )!.Sessions;
        Assert.Equal(2, sessions.Count);
        Assert.Equal(1, (await ViewAsync(driver, station.Id, 1)).Session!.ConnectorId);
        Assert.Equal(2, (await ViewAsync(driver, station.Id, 2)).Session!.ConnectorId);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await driver.GetAsync($"/api/v1/driver/stations/{station.Id}/charging?connector=3")
            ).StatusCode
        );
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = first,
                meterStop = 3000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(
            2,
            Assert
                .Single(
                    (
                        await driver.GetFromJsonAsync<DriverChargingState>(
                            "/api/v1/driver/charging-sessions"
                        )
                    )!.Sessions
                )
                .ConnectorId
        );
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = second,
                meterStop = 4000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(
            2,
            (await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history"))!.Length
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}

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
    [Fact]
    public async Task RenamingPhysicalIdentityPreservesDriverHistoryAndConnectorsAndChangesUrlAdmission()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "CH-001");
        var device = (await DeviceAsync(admin, station.Id, "CH-001")).Device;
        device = await AssignAsync(admin, station.Id, device, driverId, "CARD-001");
        using var socket = await SocketAsync(device.Identity);
        var started = DateTimeOffset.UtcNow.AddMinutes(-15);
        var transaction = await StartPhysicalAsync(socket, 2, "CARD-001", started);
        var endpoint = $"/api/v1/admin/stations/{station.Id}/device/identity";
        var input = new DeviceIdentityInput(device.Version, "NKR2023072805");
        var busy = await admin.PutAsJsonAsync(endpoint, input, timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        Assert.Contains("station_busy", await busy.Content.ReadAsStringAsync());
        await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 5000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        var response = await admin.PutAsJsonAsync(endpoint, input, timeout.Token);
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<DeviceDto>(timeout.Token))!;
        Assert.Equal("NKR2023072805", updated.Identity);
        Assert.Equal(device.Id, updated.Id);
        Assert.Equal(device.SerialNumber, updated.SerialNumber);
        Assert.Equal(device.Connectors, updated.Connectors);
        Assert.Equal(driverId, updated.AssignedDriverId);
        Assert.Equal("CARD-001", updated.CardIdTag);
        Assert.Equal(device.AuthorizedTagCount, updated.AuthorizedTagCount);
        Assert.Equal(device.Version + 1, updated.Version);
        // An admitted socket belongs to the same device key and can still report.
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        await Assert.ThrowsAsync<InvalidOperationException>(() => SocketAsync("CH-001"));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        using var reconnected = await SocketAsync(updated.Identity);
        var boot = await CallAsync(
            reconnected,
            "BootNotification",
            new { chargePointVendor = "Test", chargePointModel = "Real" }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        var history = (
            await driver.GetFromJsonAsync<DriverHistoryDto[]>("/api/v1/driver/history")
        )!;
        Assert.Equal(started, Assert.Single(history).StartedAt);
        Assert.Equal(4m, history[0].EnergyKWh);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal("CH-001", (await db.Stations.SingleAsync()).Code);
            Assert.All(
                await db.OcppConnectors.ToListAsync(),
                c => Assert.Equal(device.Id, c.ChargePointId)
            );
            Assert.Equal(driverId, (await db.ChargingConnectionRequests.SingleAsync()).DriverId);
            var audit = Assert.Single(
                await db.Audit.Where(x => x.Action == "DeviceIdentityChanged").ToListAsync()
            );
            Assert.Contains("CH-001", audit.ChangesJson!);
            Assert.Contains("NKR2023072805", audit.ChangesJson!);
            Assert.Contains(
                await db.RealtimeOutbox.ToListAsync(),
                x => x.EventType == "DeviceChanged" && x.EntityId == device.Id
            );
        }
        await reconnected.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("CP-001\n")]
    [InlineData("CP 001")]
    [InlineData("../CP-001")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789012345")]
    [InlineData("SIM-001")]
    public async Task InvalidIdentityCannotChangeRegisteredDevice(string? identity)
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var station = await StationAsync(admin, "CH-001");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device/identity",
            new DeviceIdentityInput(device.Version, identity!),
            timeout.Token
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = (
            await admin.GetFromJsonAsync<DeviceDto>($"/api/v1/admin/stations/{station.Id}/device")
        )!;
        Assert.Equal(device.Identity, unchanged.Identity);
        Assert.Equal(device.Version, unchanged.Version);
    }

    [Fact]
    public async Task RenamingIdentityChecksAuthorizationVersionAndUniqueness()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (otherAdmin, _) = await UserAsync("other@test.com", UserRole.Admin);
        var (driver, _) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(admin, "CH-001");
        var otherStation = await StationAsync(admin, "CH-002");
        var device = (await DeviceAsync(admin, station.Id, "CP-001")).Device;
        await DeviceAsync(admin, otherStation.Id, "CP-002");
        var endpoint = $"/api/v1/admin/stations/{station.Id}/device/identity";
        var input = new DeviceIdentityInput(device.Version, "NKR2023072805");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.PutAsJsonAsync(endpoint, input)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherAdmin.PutAsJsonAsync(endpoint, input)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (
                await admin.PutAsJsonAsync(endpoint, input with { Version = device.Version - 1 })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync(endpoint, input with { Identity = "CP-002" })).StatusCode
        );
        var unchanged = (
            await admin.GetFromJsonAsync<DeviceDto>($"/api/v1/admin/stations/{station.Id}/device")
        )!;
        Assert.Equal(device.Identity, unchanged.Identity);
        Assert.Equal(device.Version, unchanged.Version);
    }
}

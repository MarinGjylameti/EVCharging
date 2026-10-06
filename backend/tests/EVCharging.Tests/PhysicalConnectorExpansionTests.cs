using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    [Fact]
    public async Task RegisteredHardwareCanAddItsSecondConnectorAndAcceptTheRealChargerMessages()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await PostAsync<StationDto>(
            admin,
            "/api/v1/admin/stations",
            Station("CH-001") with
            {
                ConnectorCount = 1,
            }
        );
        var device = (await DeviceAsync(admin, station.Id, "NKR2023072805")).Device;
        device = await AssignAsync(admin, station.Id, device, driverId, null);
        Guid originalConnector;
        using (var scope = factory.Services.CreateScope())
            originalConnector = (
                await scope
                    .ServiceProvider.GetRequiredService<ChargingDbContext>()
                    .ChargingConnectors.SingleAsync()
            ).Id;
        using var socket = await SocketAsync(device.Identity);
        // Values from the hardware trace supplied by the operator.
        var boot = await CallAsync(
            socket,
            "BootNotification",
            new
            {
                chargePointModel = "BDCC220C220",
                chargePointSerialNumber = "NKR2023072805",
                chargePointVendor = "com.cnchargepoint",
                firmwareVersion = "v4.8.0-d9_S35.00.31.3.cb11200000",
            }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        var unregistered = await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 2,
                status = "Available",
                errorCode = "NoError",
                vendorId = "com.cnchargepoint",
            }
        );
        Assert.Equal(3, unregistered[0].GetInt32());
        station = Assert.Single(
            (await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!
        );
        var response = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}",
            Station(station.Code, version: station.Version, state: station.Status) with
            {
                ConnectorCount = 2,
            }
        );
        response.EnsureSuccessStatusCode();
        for (var connector = 0; connector <= 2; connector++)
        {
            var status = await CallAsync(
                socket,
                "StatusNotification",
                new
                {
                    connectorId = connector,
                    status = "Available",
                    timestamp = DateTimeOffset.UtcNow,
                    errorCode = "NoError",
                    vendorId = "com.cnchargepoint",
                }
            );
            Assert.Equal(3, status[0].GetInt32());
        }
        var view = await ViewAsync(driver, station.Id, 2);
        Assert.Equal(2, view.Connector!.Number);
        Assert.True(view.DeviceConnected);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal(
                originalConnector,
                (await db.ChargingConnectors.SingleAsync(x => x.Number == 1)).Id
            );
            Assert.Equal(2, await db.OcppConnectors.CountAsync());
            Assert.All(
                await db.ChargingConnectors.ToListAsync(),
                c =>
                {
                    Assert.True(c.IsOnline);
                    Assert.Equal(ChargingState.Available, c.Status);
                }
            );
            var stored = await db.OcppChargePoints.SingleAsync();
            Assert.Equal(device.Id, stored.Id);
            Assert.Equal(driverId, stored.AssignedDriverId);
            Assert.Equal("NKR2023072805", stored.ChargePointSerialNumber);
            Assert.Equal("v4.8.0-d9_S35.00.31.3.cb11200000", stored.FirmwareVersion);
        }
        var transaction = await StartPhysicalAsync(
            socket,
            2,
            "ANY-DEMO-CARD",
            DateTimeOffset.UtcNow
        );
        station = Assert.Single(
            (await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await admin.PatchAsJsonAsync(
                    $"/api/v1/admin/stations/{station.Id}",
                    Station(station.Code, version: station.Version, state: station.Status) with
                    {
                        ConnectorCount = 3,
                    }
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
        station = Assert.Single(
            (await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!
        );
        var reduction = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}",
            Station(station.Code, version: station.Version, state: station.Status) with
            {
                ConnectorCount = 1,
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, reduction.StatusCode);
        Assert.Contains("device_connectors_locked", await reduction.Content.ReadAsStringAsync());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}

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
    public async Task HardwareConnectsBeforeRegistrationAndRetainsUnmappedChargingUntilAssociation()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (driver, driverId) = await UserAsync("driver@test.com", UserRole.Driver);
        const string identity = "NKR2023072805";
        using var socket = await SocketAsync(identity);
        var boot = await CallAsync(
            socket,
            "BootNotification",
            new
            {
                chargePointModel = "BDCC220C220",
                chargePointSerialNumber = identity,
                chargePointVendor = "com.cnchargepoint",
                firmwareVersion = "v4.8.0-d9_S35.00.31.3.cb11200000",
            }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        // Some hardware reports whole-device availability before connector discovery.
        for (var number = 0; number <= 2; number++)
            Assert.Equal(3, (
                    await CallAsync(
                        socket,
                        "StatusNotification",
                        new
                        {
                            connectorId = number,
                            status = "Available",
                            errorCode = "NoError",
                            timestamp = DateTimeOffset.UtcNow,
                            vendorId = "com.cnchargepoint",
                        }
                    )
                )[0].GetInt32());
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        var authorization = await CallAsync(socket, "Authorize", new { idTag = "UNMAPPED-CARD" });
        Assert.Equal(
            "Accepted",
            authorization[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var started = DateTimeOffset.UtcNow;
        var transaction = await StartPhysicalAsync(socket, 1, "UNMAPPED-CARD", started);
        Assert.Equal(3, (
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
                                timestamp = started.AddSeconds(1),
                                sampledValue = new[] { new { value = "1500", unit = "Wh" } },
                            },
                        },
                    }
                )
            )[0].GetInt32());
        Guid deviceId,
            connectorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = await db.OcppChargePoints.SingleAsync();
            deviceId = device.Id;
            connectorId = (await db.OcppConnectors.SingleAsync(x => x.ConnectorNumber == 1)).Id;
            Assert.Equal("Available", device.ReportedStatus);
            Assert.Equal(2, await db.OcppConnectors.CountAsync());
            Assert.False(await db.Stations.AnyAsync());
            Assert.False(await db.ChargingSessions.AnyAsync());
            Assert.Single(await db.OcppMeterSamples.ToListAsync());
            Assert.Null((await db.OcppChargingTransactions.SingleAsync()).SessionId);
        }
        var station = await PostAsync<StationDto>(
            admin,
            "/api/v1/admin/stations",
            Station("CH-001") with
            {
                ConnectorCount = 2,
            }
        );
        // Active unmapped charging must finish before the operator changes its association.
        var input = new DeviceInput(identity, identity, null, null, "1.6J", []);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await admin.PostAsJsonAsync($"/api/v1/admin/stations/{station.Id}/device", input)
            ).StatusCode
        );
        Assert.Equal(3, (
                await CallAsync(
                    socket,
                    "StopTransaction",
                    new
                    {
                        transactionId = transaction,
                        meterStop = 2000,
                        timestamp = started.AddSeconds(2),
                        reason = "Local",
                    }
                )
            )[0].GetInt32());
        var associated = await PostAsync<DeviceConnectionDetails>(
            admin,
            $"/api/v1/admin/stations/{station.Id}/device",
            input
        );
        Assert.Equal(deviceId, associated.Device.Id);
        Assert.Equal("BDCC220C220", associated.Device.Model);
        var assigned = await AssignAsync(admin, station.Id, associated.Device, driverId, null);
        Assert.Equal(3, (await CallAsync(socket, "Heartbeat", new { }))[0].GetInt32());
        for (var number = 1; number <= 2; number++)
            await CallAsync(
                socket,
                "StatusNotification",
                new
                {
                    connectorId = number,
                    status = "Available",
                    errorCode = "NoError",
                    timestamp = DateTimeOffset.UtcNow,
                }
            );
        await StartPhysicalAsync(socket, 2, "DEMO-CARD", DateTimeOffset.UtcNow);
        Assert.NotNull((await ViewAsync(driver, station.Id, 2)).Session);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.Equal(
                connectorId,
                (await db.OcppConnectors.SingleAsync(x => x.ConnectorNumber == 1)).Id
            );
            Assert.Single(await db.OcppChargePoints.ToListAsync());
            Assert.Equal(2, await db.OcppChargingTransactions.CountAsync());
            Assert.Single(await db.ChargingSessions.ToListAsync());
            Assert.All(
                await db.OcppConnections.ToListAsync(),
                c => Assert.Equal(deviceId, c.ChargePointId)
            );
        }
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        using var reconnected = await SocketAsync(identity);
        Assert.Equal(3, (await CallAsync(reconnected, "Heartbeat", new { }))[0].GetInt32());
        await reconnected.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}

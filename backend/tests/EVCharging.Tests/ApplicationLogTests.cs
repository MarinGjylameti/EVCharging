using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    [Fact]
    public async Task SuperadminCanInspectUnknownChargerAttemptsAndProtocolFaultsWithoutExposingCardsOrCredentials()
    {
        var (admin, _) = await UserAsync("admin@test.com", UserRole.Superadmin);
        var (operatorClient, _) = await UserAsync("operator@test.com", UserRole.Admin);
        var (driver, _) = await UserAsync("driver@test.com", UserRole.Driver);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.GetAsync("/api/v1/admin/logs")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/logs")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/v1/admin/logs?source=Database")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.GetAsync("/ws/cs/UNKNOWN-DEVICE")).StatusCode
        );
        using (var discovered = await SocketAsync("UNKNOWN-DEVICE"))
            await discovered.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        var unknown = (
            await admin.GetFromJsonAsync<ApplicationLogPage>(
                "/api/v1/admin/logs?source=OCPP&identity=UNKNOWN"
            )
        )!;
        Assert.Contains(
            unknown.Entries,
            x => x.Identity == "UNKNOWN-DEVICE" && x.Message.Contains("HTTP 400")
        );
        Assert.Contains(
            unknown.Entries,
            x => x.Identity == "UNKNOWN-DEVICE" && x.Message.Contains("discovered automatically")
        );
        var station = await StationAsync(admin, "CH-001");
        var device = (await DeviceAsync(admin, station.Id, "NKR2023072805")).Device;
        using var socket = await SocketAsync(device.Identity);
        await CallAsync(
            socket,
            "BootNotification",
            new { chargePointVendor = "com.cnchargepoint", chargePointModel = "BDCC220C220" }
        );
        await CallAsync(socket, "Heartbeat", new { });
        await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "PRIVATE-CARD-CODE",
                meterStart = 1000,
                timestamp = DateTimeOffset.UtcNow,
            }
        );
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        var logs = (
            await admin.GetFromJsonAsync<ApplicationLogPage>(
                "/api/v1/admin/logs?source=OCPP&identity=NKR"
            )
        )!;
        Assert.All(logs.Entries, x => Assert.Equal(device.Identity, x.Identity));
        Assert.Contains(logs.Entries, x => x.Message.Contains("connected."));
        Assert.Contains(
            logs.Entries,
            x => x.Message.Contains("BootNotification accepted and committed")
        );
        Assert.Contains(logs.Entries, x => x.Message.Contains("Heartbeat processed and committed"));
        Assert.Contains(
            logs.Entries,
            x =>
                x.Level == "Warning"
                && x.Message.Contains("StartTransaction")
                && x.Message.Contains("SecurityError")
        );
        Assert.DoesNotContain(
            logs.Entries,
            x =>
                x.Message.Contains("PRIVATE-CARD-CODE")
                || x.Message.Contains("integration-test-passphrase")
        );
        var http = (
            await admin.GetFromJsonAsync<ApplicationLogPage>("/api/v1/admin/logs?source=HTTP")
        )!;
        Assert.Contains(
            http.Entries,
            x => x.Message.Contains("POST /api/v1/auth/register → HTTP 200")
        );
        Assert.DoesNotContain(
            http.Entries,
            x =>
                x.Message.Contains("admin@test.com")
                || x.Message.Contains("integration-test-passphrase")
                || x.Message.Contains("/admin/logs")
        );
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EVCharging.Tests;

public sealed partial class OperationsManagementTests
{
    [Fact]
    public async Task TransactionLogsCorrelateStoredMessagesAndSessionWithoutNormalMeterNoise()
    {
        var (admin, _) = await UserAsync("log-admin@test.com", UserRole.Superadmin);
        var station = await StationAsync(admin, "LOG-001");
        var device = (await DeviceAsync(admin, station.Id)).Device;
        using var socket = await SocketAsync(device.Identity);
        var started = DateTimeOffset.UtcNow;
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 1,
                idTag = "CARD-001",
                meterStart = 1000,
                timestamp = started,
            }
        );
        Assert.Equal(3, start[0].GetInt32());
        var transactionId = start[2].GetProperty("transactionId").GetInt32();
        var meter = await CallAsync(
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
                        timestamp = started.AddSeconds(1),
                        sampledValue = new[] { new { value = "2000", unit = "Wh" } },
                    },
                },
            }
        );
        Assert.Equal(3, meter[0].GetInt32());
        var rejected = await CallAsync(
            socket,
            "MeterValues",
            new { connectorId = "private-invalid-value", transactionId }
        );
        Assert.Equal(4, rejected[0].GetInt32());
        var stopped = await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId,
                meterStop = 2000,
                timestamp = started.AddSeconds(2),
                reason = "Local",
            }
        );
        Assert.Equal(3, stopped[0].GetInt32());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        await factory
            .Services.GetServices<IHostedService>()
            .OfType<ApplicationLogWorker>()
            .Single()
            .FlushAsync(timeout.Token);
        var logs = (
            await admin.GetFromJsonAsync<ApplicationLogHistory>(
                "/api/v1/admin/logs/history?source=OCPP&identity=" + device.Identity
            )
        )!.Entries;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var session = await db.ChargingSessions.SingleAsync();
        foreach (var action in new[] { "StartTransaction", "StopTransaction" })
        {
            var entry = Assert.Single(
                logs,
                x => x.Context?.Action == action && x.Context.EventName == "message_committed"
            );
            Assert.Equal(session.Id, entry.Context!.SessionId);
            Assert.Equal(station.Id, entry.Context.StationId);
            Assert.Equal(session.ConnectorId, entry.Context.ConnectorId);
            Assert.Equal(1, entry.Context.ConnectorNumber);
            var inbox = await db.OcppInboundMessages.SingleAsync(x =>
                x.Id == entry.Context.InboxMessageId
            );
            Assert.Equal(inbox.CallId, entry.Context.MessageId);
            Assert.Equal(inbox.ConnectionId, entry.Context.ConnectionId);
            Assert.NotNull(entry.Context.TraceId);
        }
        Assert.DoesNotContain(
            logs,
            x => x.Context?.Action == "MeterValues" && x.Context.MessageId == meter[1].GetString()
        );
        Assert.Contains(
            logs,
            x =>
                x.Context?.Action == "MeterValues"
                && x.Context.MessageId == rejected[1].GetString()
                && x.Level == "Warning"
                && x.Context.EventName == "message_rejected"
                && x.Context.InboxMessageId is not null
        );
        Assert.DoesNotContain(
            logs,
            x => x.Message.Contains("CARD-001") || x.Message.Contains("private-invalid-value")
        );
        Assert.Equal(1, await db.OcppMeterSamples.CountAsync());
        var meterCallId = meter[1].GetString();
        Assert.Equal(
            "Processed",
            (await db.OcppInboundMessages.SingleAsync(x => x.CallId == meterCallId)).ProcessingState
        );
    }

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
            HttpStatusCode.Forbidden,
            (await operatorClient.GetAsync("/api/v1/admin/logs/history")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/logs/history")).StatusCode
        );
        foreach (
            var filter in new[]
            {
                "source=Database",
                "level=Unknown",
                "limit=1001",
                "beforeId=0",
                "from=2026-10-08T00:00:00Z&until=2026-10-07T00:00:00Z",
            }
        )
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await admin.GetAsync("/api/v1/admin/logs/history?" + filter)).StatusCode
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
        var connected = Assert.Single(
            logs.Entries,
            x => x.Context?.EventName == "websocket_connected"
        );
        Assert.NotNull(connected.Context!.TraceId);
        Assert.NotNull(connected.Context.ConnectionId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var closed = await db.OcppConnections.SingleAsync(x =>
                x.Id == connected.Context.ConnectionId
            );
            Assert.Equal("peer_close", closed.CloseReason);
            Assert.Equal(1000, closed.CloseCode);
        }
        var heartbeat = Assert.Single(
            logs.Entries,
            x => x.Context?.Action == "Heartbeat" && x.Context.EventName == "message_committed"
        );
        Assert.Equal(connected.Context.TraceId, heartbeat.Context!.TraceId);
        Assert.Equal(connected.Context.ConnectionId, heartbeat.Context.ConnectionId);
        Assert.Contains(
            logs.Entries,
            x =>
                x.Context?.EventName == "reply_sent"
                && x.Context.MessageId == heartbeat.Context.MessageId
        );
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
        await factory
            .Services.GetServices<IHostedService>()
            .OfType<ApplicationLogWorker>()
            .Single()
            .FlushAsync(timeout.Token);
        var history = (
            await admin.GetFromJsonAsync<ApplicationLogHistory>(
                "/api/v1/admin/logs/history?source=OCPP&traceId="
                    + Uri.EscapeDataString(connected.Context.TraceId!)
            )
        )!;
        Assert.Contains(history.Entries, x => x.EventId == connected.EventId);
        Assert.All(
            history.Entries,
            x => Assert.Equal(connected.Context.TraceId, x.Context!.TraceId)
        );
        Assert.DoesNotContain(history.Entries, x => x.Message.Contains("PRIVATE-CARD-CODE"));
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

    [Fact]
    public async Task OrdinaryHttpOnCompatibilityPathAndRequestIdsAreRecordedWithoutQueriesOrHeaders()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Request-ID", "client-supplied-secret");
        var response = await client.GetAsync("/ws/cs/CP-001/CP-001?password=secret-query");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var trace = Assert.Single(response.Headers.GetValues("X-Request-ID"));
        Assert.NotEqual("client-supplied-secret", trace);
        var logs = factory
            .Services.GetRequiredService<IApplicationDiagnostics>()
            .Read("OCPP")
            .Entries;
        Assert.Contains(
            logs,
            x =>
                x.Context?.EventName == "handshake_received"
                && x.Context.TraceId == trace
                && x.Message.Contains("routeMatched=False")
        );
        Assert.Contains(logs, x => x.Context?.HttpStatus == 404 && x.Context.TraceId == trace);
        Assert.DoesNotContain(
            logs,
            x => x.Message.Contains("secret-query") || x.Message.Contains("client-supplied-secret")
        );
    }

    [Fact]
    public async Task MalformedFrameProducesCorrelatedTransportDiagnosticsWithoutItsContents()
    {
        using var socket = await SocketAsync("CP-BAD-FRAME");
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("{private-card-secret"),
            WebSocketMessageType.Text,
            true,
            timeout.Token
        );
        var received = await socket.ReceiveAsync(new byte[2048], timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, received.CloseStatus);
        var logs = factory
            .Services.GetRequiredService<IApplicationDiagnostics>()
            .Read("OCPP", "CP-BAD-FRAME")
            .Entries;
        var failure = Assert.Single(logs, x => x.Context?.EventName == "transport_disconnected");
        Assert.Equal("invalid_payload", failure.Context!.ErrorCode);
        Assert.NotNull(failure.Context.TraceId);
        Assert.NotNull(failure.Context.ConnectionId);
        Assert.DoesNotContain(logs, x => x.Message.Contains("private-card-secret"));
    }
}

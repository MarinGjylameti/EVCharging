using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Identity;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OperationsManagementTests : IDisposable
{
    private readonly ApiFactory factory = new();
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(40));

    private async Task<(HttpClient Client, Guid Id)> UserAsync(string email, UserRole role)
    {
        var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                email,
                "integration-test-passphrase",
                "Arta",
                "Test",
                "sq",
                "+355691234567"
            ),
            timeout.Token
        );
        registration.EnsureSuccessStatusCode();
        var auth = (await registration.Content.ReadFromJsonAsync<AuthResult>(timeout.Token))!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(a => a.Id == auth.User.Id)).Role = role;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        return (client, auth.User.Id);
    }

    private async Task<T> PostAsync<T>(HttpClient client, string path, object input)
    {
        var response = await client.PostAsJsonAsync(path, input, timeout.Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(timeout.Token))!;
    }

    private Task<LocationDto> LocationAsync(HttpClient client, string name = "Site") =>
        PostAsync<LocationDto>(
            client,
            "/api/v1/admin/locations",
            new LocationInput(name, "Tirane", "Street 123", 41.32, 19.81)
        );

    private static StationInput Station(
        string code,
        Guid? locationId = null,
        long version = 0,
        decimal price = .35m,
        string state = "Available",
        string? reason = null
    ) =>
        new(
            code,
            "Physical " + code,
            "Tirane",
            "Street 123",
            41.32,
            19.81,
            "CCS2",
            2,
            50,
            price,
            state,
            version,
            locationId,
            reason
        );

    private Task<StationDto> StationAsync(
        HttpClient client,
        string code,
        Guid? locationId = null
    ) => PostAsync<StationDto>(client, "/api/v1/admin/stations", Station(code, locationId));

    private Task<DeviceCredentials> DeviceAsync(
        HttpClient client,
        Guid stationId,
        string identity = "CP-001"
    ) =>
        PostAsync<DeviceCredentials>(
            client,
            $"/api/v1/admin/stations/{stationId}/device",
            new DeviceInput(identity, "SERIAL-123", null, null, "1.6J", ["CARD-001"])
        );

    private async Task<WebSocket> SocketAsync(string identity, string password)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("ocpp1.6");
        client.ConfigureRequest = r =>
            r.Headers.Authorization =
                "Basic "
                + Convert.ToBase64String(Encoding.UTF8.GetBytes(identity + ":" + password));
        return await client.ConnectAsync(
            new Uri("ws://localhost/ws/cs/" + identity),
            timeout.Token
        );
    }

    private async Task<JsonElement> CallAsync(WebSocket socket, string action, object payload)
    {
        await socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(
                new object[] { 2, Guid.NewGuid().ToString(), action, payload }
            ),
            WebSocketMessageType.Text,
            true,
            timeout.Token
        );
        var buffer = new byte[16384];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.True(result.EndOfMessage);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonDocument.Parse(buffer.AsSpan(0, result.Count).ToArray()).RootElement.Clone();
    }

    [Fact]
    public async Task PhysicalDeviceWorksWithSimulatorDisabledAndKeepsSessionsPrivate()
    {
        var (super, _) = await UserAsync("super@test.com", UserRole.Superadmin);
        var (driver, _) = await UserAsync("driver@test.com", UserRole.Driver);
        var station = await StationAsync(super, "REAL-001");
        var credentials = await DeviceAsync(super, station.Id);
        Assert.Equal("CP-001", credentials.Username);
        Assert.Equal("/ws/cs/CP-001", credentials.WebSocketPath);
        Assert.Equal(48, credentials.Password.Length);
        var deviceResponse = await super.GetAsync($"/api/v1/admin/stations/{station.Id}/device");
        Assert.DoesNotContain(
            credentials.Password,
            await deviceResponse.Content.ReadAsStringAsync()
        );
        Assert.DoesNotContain("passwordHash", await deviceResponse.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            Assert.NotEqual(
                credentials.Password,
                (await db.OcppChargePoints.SingleAsync()).PasswordHash
            );
            Assert.DoesNotContain(
                credentials.Password,
                string.Join("", await db.Audit.Select(a => a.ChangesJson).ToListAsync())
            );
            Assert.Equal(2, await db.OcppConnectors.CountAsync());
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SocketAsync("CP-001", "wrong-password")
        );
        using var socket = await SocketAsync("CP-001", credentials.Password);
        var boot = await CallAsync(
            socket,
            "BootNotification",
            new { chargePointVendor = "Custom", chargePointModel = "Handmade" }
        );
        Assert.Equal("Accepted", boot[2].GetProperty("status").GetString());
        var invalid = await CallAsync(socket, "Authorize", new { idTag = "UNKNOWN" });
        Assert.Equal(
            "Invalid",
            invalid[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var valid = await CallAsync(socket, "Authorize", new { idTag = "CARD-001" });
        Assert.Equal(
            "Accepted",
            valid[2].GetProperty("idTagInfo").GetProperty("status").GetString()
        );
        var started = DateTimeOffset.UtcNow;
        var start = await CallAsync(
            socket,
            "StartTransaction",
            new
            {
                connectorId = 2,
                idTag = "CARD-001",
                meterStart = 1000,
                timestamp = started,
            }
        );
        Assert.Equal(3, start[0].GetInt32());
        var transaction = start[2].GetProperty("transactionId").GetInt32();
        await CallAsync(
            socket,
            "StatusNotification",
            new
            {
                connectorId = 2,
                status = "Charging",
                errorCode = "NoError",
                timestamp = started.AddSeconds(1),
            }
        );
        var meter = await CallAsync(
            socket,
            "MeterValues",
            new
            {
                connectorId = 2,
                transactionId = transaction,
                meterValue = new[]
                {
                    new
                    {
                        timestamp = started.AddSeconds(2),
                        sampledValue = new[]
                        {
                            new
                            {
                                value = "3000",
                                measurand = "Energy.Active.Import.Register",
                                unit = "Wh",
                            },
                            new
                            {
                                value = "22000",
                                measurand = "Power.Active.Import",
                                unit = "W",
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
            }
        );
        Assert.Equal(3, meter[0].GetInt32());
        var sessions = (await super.GetFromJsonAsync<SessionPage>("/api/v1/admin/sessions"))!;
        var current = Assert.Single(sessions.Items);
        Assert.Equal(2m, current.EnergyKWh);
        Assert.Equal(22m, current.PowerKW);
        Assert.Equal(45m, current.StateOfCharge);
        Assert.Equal(2, current.ConnectorNumber);
        Assert.Null(current.DriverId);
        var driverCharging = (
            await driver.GetFromJsonAsync<DriverStationCharging>(
                $"/api/v1/driver/stations/{station.Id}/charging"
            )
        )!;
        Assert.Null(driverCharging.Session);
        Assert.Null(driverCharging.Diagnostics);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/sessions")).StatusCode
        );
        var busy = await super.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device",
            new DeviceControlInput(credentials.Device.Version, true, true)
        );
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        var stop = await CallAsync(
            socket,
            "StopTransaction",
            new
            {
                transactionId = transaction,
                meterStop = 5000,
                timestamp = started.AddSeconds(3),
                reason = "EVDisconnected",
            }
        );
        Assert.Equal(3, stop[0].GetInt32());
        current = Assert.Single(
            (await super.GetFromJsonAsync<SessionPage>("/api/v1/admin/sessions"))!.Items
        );
        Assert.Equal("Completed", current.Status);
        Assert.Equal(4m, current.EnergyKWh);
        Assert.Equal(1.4m, current.Cost);
        var summary = (await super.GetFromJsonAsync<OperationsSummary>("/api/v1/admin/summary"))!;
        Assert.Equal(0, summary.ActiveSessions);
        Assert.Equal(1, summary.CompletedSessions);
        Assert.Equal(4m, summary.EnergyKWh);
        Assert.Equal(1.4m, summary.CalculatedCost);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task AdministratorAssignmentsRestrictReadsWritesQueriesAndProvisioning()
    {
        var (super, _) = await UserAsync("super@test.com", UserRole.Superadmin);
        var (admin, adminId) = await UserAsync("admin@test.com", UserRole.Admin);
        var location = await LocationAsync(super);
        var otherLocation = await LocationAsync(super, "Other");
        var allowed = await StationAsync(super, "ALLOW-001", location.Id);
        var denied = await StationAsync(super, "DENY-001", otherLocation.Id);
        var response = await super.PatchAsJsonAsync(
            $"/api/v1/admin/administrators/{adminId}",
            new AdministratorInput(
                "admin@test.com",
                "Arta",
                "Test",
                null,
                null,
                true,
                [location.Id],
                [],
                1
            )
        );
        response.EnsureSuccessStatusCode();
        var points = (await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!;
        Assert.Equal(allowed.Id, Assert.Single(points).Id);
        Assert.Single((await admin.GetFromJsonAsync<LocationDto[]>("/api/v1/admin/locations"))!);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await admin.PatchAsJsonAsync(
                    $"/api/v1/admin/stations/{denied.Id}",
                    Station("DENY-001", otherLocation.Id)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/admin/stations/{denied.Id}/device")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/stations",
                    Station("NEW-001", otherLocation.Id)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/v1/admin/drivers")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/v1/admin/administrators")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/locations",
                    new LocationInput("No", "City", "Street", 41, 19)
                )
            ).StatusCode
        );
        var added = await StationAsync(admin, "NEW-002", location.Id);
        var updated = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{allowed.Id}",
            Station("ALLOW-001", location.Id, price: .4m, reason: "New tariff")
        );
        updated.EnsureSuccessStatusCode();
        var prices = (await admin.GetFromJsonAsync<PriceRevisionDto[]>("/api/v1/admin/prices"))!;
        Assert.DoesNotContain(prices, x => x.StationId == denied.Id);
        Assert.Contains(prices, x => x.PreviousPrice == .35m && x.PricePerKWh == .4m);
        var audit = (await admin.GetFromJsonAsync<AuditDto[]>("/api/v1/admin/audit"))!;
        Assert.DoesNotContain(audit, x => x.EntityId == denied.Id);
        Assert.Contains(audit, x => x.ActorRole == "Admin" && x.Action == "StationUpdated");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await admin.PatchAsJsonAsync(
                    $"/api/v1/admin/stations/{added.Id}",
                    Station("NEW-002", otherLocation.Id)
                )
            ).StatusCode
        );
        var latest = (
            await super.GetFromJsonAsync<AdministratorDetails[]>("/api/v1/admin/administrators")
        )!.Single(x => x.Id == adminId);
        (
            await super.PatchAsJsonAsync(
                $"/api/v1/admin/administrators/{adminId}",
                new AdministratorInput(
                    latest.Email,
                    latest.FirstName,
                    latest.LastName,
                    null,
                    null,
                    true,
                    [],
                    [allowed.Id],
                    latest.Version
                )
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            allowed.Id,
            Assert
                .Single((await admin.GetFromJsonAsync<StationDto[]>("/api/v1/admin/stations"))!)
                .Id
        );
        Assert.Empty((await admin.GetFromJsonAsync<LocationDto[]>("/api/v1/admin/locations"))!);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/stations",
                    Station("NEW-003", location.Id)
                )
            ).StatusCode
        );
        var ownedEdit = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{allowed.Id}",
            Station("ALLOW-001", location.Id, version: 1, price: .4m)
        );
        ownedEdit.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreatingEditingDeactivatingAndArchivingAdministratorsRevokesSessionsAndPreservesAudit()
    {
        var (super, _) = await UserAsync("super@test.com", UserRole.Superadmin);
        var location = await LocationAsync(super);
        var created = await PostAsync<AdministratorDetails>(
            super,
            "/api/v1/admin/administrators",
            new AdministratorInput(
                "new@test.com",
                "New",
                "Admin",
                "+355691234567",
                "new-admin-passphrase",
                true,
                [location.Id],
                []
            )
        );
        Assert.True(created.IsActive);
        Assert.Equal(location.Id, Assert.Single(created.LocationIds));
        var login = factory.CreateClient();
        var authResponse = await login.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("new@test.com", "new-admin-passphrase")
        );
        authResponse.EnsureSuccessStatusCode();
        var auth = (await authResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Equal("ADMIN", auth.User.Role);
        login.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var latest = (
            await super.GetFromJsonAsync<AdministratorDetails[]>("/api/v1/admin/administrators")
        )!.Single();
        var update = await super.PatchAsJsonAsync(
            $"/api/v1/admin/administrators/{created.Id}",
            new AdministratorInput(
                latest.Email,
                "Changed",
                "Admin",
                null,
                null,
                false,
                latest.LocationIds,
                [],
                latest.Version
            )
        );
        update.EnsureSuccessStatusCode();
        var changed = (await update.Content.ReadFromJsonAsync<AdministratorDetails>())!;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await login.GetAsync("/api/v1/admin/stations")).StatusCode
        );
        var archived = await PostAsync<AdministratorDetails>(
            super,
            $"/api/v1/admin/administrators/{created.Id}/archive",
            new { changed.Version }
        );
        Assert.NotNull(archived.ArchivedAt);
        Assert.False(archived.IsActive);
        Assert.Contains(
            (await super.GetFromJsonAsync<AuditDto[]>("/api/v1/admin/audit"))!,
            x => x.Action == "AdministratorArchived"
        );
        Assert.DoesNotContain(
            "new-admin-passphrase",
            await (await super.GetAsync("/api/v1/admin/audit")).Content.ReadAsStringAsync()
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await login.PostAsJsonAsync(
                    "/api/v1/auth/login",
                    new LoginRequest(latest.Email, "new-admin-passphrase")
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task LocationArchiveAndManualChangesValidateVersionsReasonAndResourceTopology()
    {
        var (super, _) = await UserAsync("super@test.com", UserRole.Superadmin);
        var location = await LocationAsync(super);
        var station = await StationAsync(super, "REAL-001", location.Id);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await super.PostAsJsonAsync(
                    $"/api/v1/admin/locations/{location.Id}/archive",
                    new { location.Version }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await super.PatchAsJsonAsync(
                    $"/api/v1/admin/stations/{station.Id}",
                    Station("REAL-001", location.Id, price: .4m)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            (
                await super.PatchAsJsonAsync(
                    $"/api/v1/admin/stations/{station.Id}",
                    Station("REAL-001", location.Id, version: 99)
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await super.PatchAsJsonAsync(
                    $"/api/v1/admin/locations/{location.Id}",
                    new LocationInput("No", "City", "Street", 91, 19, location.Version)
                )
            ).StatusCode
        );
        (
            await super.PostAsJsonAsync(
                $"/api/v1/admin/stations/{station.Id}/archive",
                new { station.Version }
            )
        ).EnsureSuccessStatusCode();
        var archived = await PostAsync<LocationDto>(
            super,
            $"/api/v1/admin/locations/{location.Id}/archive",
            new { location.Version }
        );
        Assert.True(archived.IsArchived);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await super.PostAsJsonAsync(
                    "/api/v1/admin/stations",
                    Station("NEW-001", location.Id)
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task CredentialRotationAndDisableInvalidateDeviceAccessWithoutLeakingSecrets()
    {
        var (super, _) = await UserAsync("super@test.com", UserRole.Superadmin);
        var station = await StationAsync(super, "REAL-001");
        var original = await DeviceAsync(super, station.Id);
        using var socket = await SocketAsync(original.Username, original.Password);
        var rotatedResponse = await super.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device",
            new DeviceControlInput(original.Device.Version, true, true)
        );
        rotatedResponse.EnsureSuccessStatusCode();
        var rotated = (await rotatedResponse.Content.ReadFromJsonAsync<DeviceCredentials>())!;
        Assert.NotEqual(original.Password, rotated.Password);
        Assert.Equal(original.Device.Version + 1, rotated.Device.Version);
        await socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(
                new object[] { 2, "revoked", "Heartbeat", new { } }
            ),
            WebSocketMessageType.Text,
            true,
            timeout.Token
        );
        var closed = await socket.ReceiveAsync(new byte[1024], timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closed.CloseStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SocketAsync(original.Username, original.Password)
        );
        using var fresh = await SocketAsync(rotated.Username, rotated.Password);
        Assert.Equal(3, (await CallAsync(fresh, "Heartbeat", new { }))[0].GetInt32());
        await fresh.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        var disable = await super.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{station.Id}/device",
            new DeviceControlInput(rotated.Device.Version, false)
        );
        disable.EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SocketAsync(rotated.Username, rotated.Password)
        );
    }

    public void Dispose()
    {
        timeout.Dispose();
        factory.Dispose();
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class StationManagementTests : IDisposable
{
    private readonly ApiFactory factory = new();

    private async Task<(HttpClient Client, Guid UserId)> Login(string email, UserRole role)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email,
                password = "a-long-demo-passphrase",
                phone = "+355691234567",
                firstName = "Demo",
                lastName = "User",
                language = "sq",
            }
        );
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(x => x.Id == auth.User.Id)).Role = role;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        return (client, auth.User.Id);
    }

    private static object Input(
        string code = "TIR-001",
        decimal price = 0.35m,
        string status = "Available",
        long version = 0
    ) =>
        new
        {
            code,
            name = "Qendra",
            city = "Tiranë",
            address = "Sheshi Skënderbej",
            latitude = 41.3275,
            longitude = 19.8187,
            connectorType = "CCS2",
            connectorCount = 2,
            powerKW = 120,
            pricePerKWh = price,
            status,
            version,
            reason = "Operator test change",
        };

    [Fact]
    public async Task SuperadminCreatesUpdatesAndArchivesWithAuditAndVersion()
    {
        var (admin, _) = await Login("super@example.test", UserRole.Superadmin);
        var (driver, driverId) = await Login("driver@example.test", UserRole.Driver);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/stations", Input());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var station = (await created.Content.ReadFromJsonAsync<JsonElement>());
        var id = station.GetProperty("id").GetGuid();
        Assert.False(station.GetProperty("isDemo").GetBoolean());
        Assert.Equal(0, station.GetProperty("version").GetInt64());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            db.ChargingHistory.Add(
                new ChargingHistory
                {
                    DriverId = driverId,
                    StationId = id,
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EndedAt = DateTimeOffset.UtcNow,
                    EnergyKWh = 20,
                    UnitPriceSnapshot = 0.35m,
                    TotalCost = 7,
                }
            );
            await db.SaveChangesAsync();
        }
        var stale = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{id}",
            Input(price: 0.40m, version: 9)
        );
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var updated = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{id}",
            Input(price: 0.40m)
        );
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var changed = (await updated.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(1, changed.GetProperty("version").GetInt64());
        Assert.Equal(0.40m, changed.GetProperty("pricePerKWh").GetDecimal());
        var history = await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/history");
        Assert.Equal(0.35m, history[0].GetProperty("unitPriceSnapshot").GetDecimal());
        var archived = await admin.PostAsJsonAsync(
            $"/api/v1/admin/stations/{id}/archive",
            new { version = 1 }
        );
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(
            0,
            (await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/stations")).GetArrayLength()
        );
        using var check = factory.Services.CreateScope();
        var audit = await check
            .ServiceProvider.GetRequiredService<ChargingDbContext>()
            .Audit.Where(x => x.EntityId == id)
            .OrderBy(x => x.RecordedAt)
            .Select(x => x.Action)
            .ToListAsync();
        Assert.Equal(["StationCreated", "StationUpdated", "StationArchived"], audit);
    }

    [Fact]
    public async Task ManualRestrictionPersistsUntilOperatorClearsIt()
    {
        var (admin, _) = await Login("restriction-admin@example.test", UserRole.Superadmin);
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/stations",
            Input(status: "OutOfService")
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var station = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = station.GetProperty("id").GetGuid();
        Assert.Equal("OutOfService", station.GetProperty("manualRestriction").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var stored = await db.Stations.SingleAsync(point => point.Id == id);
            Assert.Equal(ManualRestriction.OutOfService, stored.ManualRestriction);
        }

        var cleared = await admin.PatchAsJsonAsync(
            $"/api/v1/admin/stations/{id}",
            Input(status: "Available")
        );
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var updated = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("manualRestriction").ValueKind);
    }

    [Fact]
    public async Task OperatorWithoutAssignmentsCannotCreateStations()
    {
        var (operatorClient, _) = await Login("admin@example.test", UserRole.Admin);
        var (driver, _) = await Login("driver@example.test", UserRole.Driver);
        Assert.Equal(
            HttpStatusCode.OK,
            (await operatorClient.GetAsync("/api/v1/admin/stations")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.PostAsJsonAsync("/api/v1/admin/stations", Input())).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await driver.GetAsync("/api/v1/admin/stations")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/v1/admin/stations")).StatusCode
        );
    }

    [Fact]
    public async Task RejectsInvalidAndBusyStations()
    {
        var (admin, _) = await Login("super@example.test", UserRole.Superadmin);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await admin.PostAsJsonAsync("/api/v1/admin/stations", Input(code: "bad code"))
            ).StatusCode
        );
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/stations",
            Input(status: "Charging")
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/stations/{id}/archive",
                    new { version = 0 }
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task DriverSeesOnlyFreshDeviceAvailabilityForecast()
    {
        var (admin, _) = await Login("forecast-admin@example.test", UserRole.Superadmin);
        var (driver, _) = await Login("forecast-driver@example.test", UserRole.Driver);
        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/stations",
            Input(status: "Charging")
        );
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var station = await db.Stations.SingleAsync(x => x.Id == id);
            AvailabilityForecastRules.SetDeviceEstimate(station, 1, now.AddMinutes(40), now);
            await db.SaveChangesAsync();
        }
        var rows = await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/stations");
        var forecast = rows[0].GetProperty("availabilityForecast");
        Assert.Equal(1, forecast.GetProperty("connectorId").GetInt32());
        Assert.Equal("Device", forecast.GetProperty("source").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var station = await db.Stations.SingleAsync(x => x.Id == id);
            station.ForecastObservedAt = now.AddMinutes(-20);
            await db.SaveChangesAsync();
        }
        rows = await driver.GetFromJsonAsync<JsonElement>("/api/v1/driver/stations");
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("availabilityForecast").ValueKind);
    }

    [Theory]
    [InlineData("Maintenance", "Physical")]
    [InlineData("OutOfService", "Physical")]
    [InlineData("Unknown", "Linked")]
    [InlineData("Available", "Waiting")]
    public async Task ArchiveChecksPhysicalSessionsAndClaimsEvenWhenPublicStatusIsNotBusy(
        string status,
        string occupancy
    )
    {
        var (admin, userId) = await Login("archive-operator@test.com", UserRole.Superadmin);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/stations", Input(status: status));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            if (occupancy == "Physical")
                db.ChargingSessions.Add(
                    new ChargingSession(id, .35m, "EUR", DateTimeOffset.UtcNow)
                );
            else
                db.ChargingConnectionRequests.Add(
                    new ChargingConnectionRequest
                    {
                        DriverId = userId,
                        StationId = id,
                        State = occupancy,
                        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                    }
                );
            await db.SaveChangesAsync();
        }
        using var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/stations/{id}/archive",
            new { version = 0 }
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "station_busy",
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code")
                .GetString()
        );
        using var check = factory.Services.CreateScope();
        var stored = check.ServiceProvider.GetRequiredService<ChargingDbContext>();
        Assert.False((await stored.Stations.SingleAsync(x => x.Id == id)).IsArchived);
        Assert.DoesNotContain(await stored.Audit.ToListAsync(), x => x.Action == "StationArchived");
    }

    public void Dispose() => factory.Dispose();
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class DriverApiTests : IDisposable
{
    private readonly ApiFactory factory = new();

    private async Task<(HttpClient Client, AuthResult Auth)> Register(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email,
                password = "long-demo-passphrase",
                firstName = "Demo",
                lastName = "Driver",
                language = "sq",
            }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            auth.AccessToken
        );
        return (client, auth);
    }

    [Fact]
    public async Task DriverSeesAvailableMapButOnlyOwnHistory()
    {
        var (first, account) = await Register("first@example.test");
        var (second, _) = await Register("second@example.test");
        Guid stationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var visible = new ChargingStation
            {
                Code = "TEST-001",
                Name = "Test Station",
                City = "Tiranë",
                Address = "Test address",
                Latitude = 41.32,
                Longitude = 19.82,
                ConnectorType = "CCS2",
                ConnectorCount = 2,
                PowerKW = 120,
                PricePerKWh = 0.35m,
                IsDemo = true,
            };
            var archived = new ChargingStation
            {
                Code = "TEST-002",
                Name = "Hidden",
                City = "Tiranë",
                Address = "Test address",
                Latitude = 41.32,
                Longitude = 19.82,
                ConnectorType = "CCS2",
                ConnectorCount = 1,
                PowerKW = 50,
                PricePerKWh = 0.40m,
                IsArchived = true,
            };
            stationId = visible.Id;
            db.Stations.AddRange(visible, archived);
            db.ChargingHistory.Add(
                new ChargingHistory
                {
                    DriverId = account.User.Id,
                    StationId = stationId,
                    Station = visible,
                    StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    EndedAt = DateTimeOffset.UtcNow,
                    EnergyKWh = 20,
                    UnitPriceSnapshot = 0.35m,
                    TotalCost = 7,
                    IsDemo = true,
                }
            );
            await db.SaveChangesAsync();
        }

        var map = await first.GetFromJsonAsync<JsonElement>("/api/v1/driver/stations");
        Assert.Equal(1, map.GetArrayLength());
        Assert.Equal(stationId, map[0].GetProperty("id").GetGuid());
        Assert.True(map[0].GetProperty("isDemo").GetBoolean());

        var own = await first.GetFromJsonAsync<JsonElement>("/api/v1/driver/history");
        Assert.Equal(1, own.GetArrayLength());
        Assert.Equal(7m, own[0].GetProperty("totalCost").GetDecimal());
        var other = await second.GetFromJsonAsync<JsonElement>("/api/v1/driver/history");
        Assert.Equal(0, other.GetArrayLength());
    }

    [Fact]
    public async Task DriverEndpointsRequireLoginAndDriverRole()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/driver/stations")).StatusCode
        );
        var (client, auth) = await Register("operator@example.test");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(x => x.Id == auth.User.Id)).Role = EVCharging
                .Domain
                .Identity
                .UserRole
                .Admin;
            await db.SaveChangesAsync();
        }
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/driver/history")).StatusCode
        );
    }

    public void Dispose() => factory.Dispose();
}

using System.Net.Http.Json;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OperationsRealtimeTests
{
    [Fact]
    public async Task ForecastOnlyUpdatesRemainPublicDeltasWithoutInvalidatingOperationsAnalytics()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var projection = scope.ServiceProvider.GetRequiredService<StationProjection>();
        var now = DateTimeOffset.UtcNow;
        var station = new EVCharging.Domain.Charging.ChargingStation
        {
            Code = "FORECAST",
            Name = "Forecast",
            City = "Tirane",
            Address = "Street",
            ConnectorCount = 1,
        };
        var connector = new EVCharging.Domain.Charging.ChargingConnector
        {
            StationId = station.Id,
            Number = 1,
            IsOnline = true,
            Status = EVCharging.Domain.Charging.ChargingState.Charging,
        };
        var session = new EVCharging.Domain.Charging.ChargingSession
        {
            PointId = station.Id,
            ConnectorId = connector.Id,
            ConnectorNumber = 1,
            StartedAt = now,
            StartReceivedAt = now,
            Status = EVCharging.Domain.Charging.ChargingState.Charging,
            BatteryCapacityKWh = 60,
            PowerKW = 22,
            PowerScope = "Total",
            StateOfCharge = 50,
            PowerReceivedAt = now,
            SocReceivedAt = now,
        };
        db.AddRange(station, connector, session);
        await db.SaveChangesAsync();
        async Task<StationChanged> ProjectAsync()
        {
            await projection.RefreshAsync(station.Id, CancellationToken.None);
            await db.SaveChangesAsync();
            var entry = await db.RealtimeOutbox.SingleAsync(x =>
                x.EntityId == station.Id && x.EntityVersion == station.Version
            );
            return JsonSerializer.Deserialize<StationChanged>(entry.PayloadJson)!;
        }
        Assert.False((await ProjectAsync()).TelemetryOnly);
        session.StateOfCharge = 55;
        session.SocReceivedAt = now.AddSeconds(1);
        var forecast = await ProjectAsync();
        Assert.True(forecast.TelemetryOnly);
        Assert.NotNull(forecast.Station);
        Assert.Equal("StationChanged", forecast.EventType);
        connector.Status = EVCharging.Domain.Charging.ChargingState.PausedByVehicle;
        Assert.False((await ProjectAsync()).TelemetryOnly);
    }

    [Fact]
    public async Task OperationsBusDeliversChangeNoticesWithoutSendingPrivateSessionDataToOtherDrivers()
    {
        var bus = new StationEventBus();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var driver = bus.Subscribe(Guid.NewGuid(), timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        await using var operations = bus.SubscribeOperations(Guid.NewGuid(), timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var driverNext = driver.MoveNextAsync().AsTask();
        var operatorNext = operations.MoveNextAsync().AsTask();
        var station = Guid.NewGuid();
        var privateId = Guid.NewGuid();
        bus.Publish(
            new(
                station,
                3,
                "Changed",
                Guid.NewGuid(),
                privateId,
                "SessionMeasured",
                "ChargingSession",
                Guid.NewGuid(),
                TelemetryOnly: true
            )
        );
        Assert.True(await operatorNext);
        Assert.Equal(privateId, operations.Current.EventId);
        Assert.Null(operations.Current.RecipientAccountId);
        Assert.Null(operations.Current.Session);
        Assert.Null(operations.Current.Command);
        Assert.True(operations.Current.TelemetryOnly);
        bus.Publish(
            new(station, 4, "Changed", EventId: Guid.NewGuid(), EventType: "SessionMeasured"),
            operationsOnly: true
        );
        var publicId = Guid.NewGuid();
        bus.Publish(new(station, 5, "Changed", EventId: publicId));
        Assert.True(await driverNext);
        Assert.Equal(publicId, driver.Current.EventId);
    }

    [Fact]
    public async Task OperatorSseFiltersEachResourceAndEndsAfterAccountRevocation()
    {
        await using var factory = new ApiFactory();
        using var http = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await http.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = "admin@sse.test",
                password = "admin-test-passphrase",
                firstName = "Test",
                lastName = "Admin",
                language = "sq",
                phone = "+355691234567",
            },
            timeout.Token
        );
        var auth = (
            await response.Content.ReadFromJsonAsync<EVCharging.Application.Identity.AuthResult>(
                timeout.Token
            )
        )!;
        var allowed = Guid.NewGuid();
        var denied = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            (await db.Accounts.SingleAsync(a => a.Id == auth.User.Id)).Role = UserRole.Admin;
            db.Stations.AddRange(
                new EVCharging.Domain.Charging.ChargingStation
                {
                    Id = allowed,
                    Code = "ALLOW",
                    Name = "Allowed",
                    City = "City",
                    Address = "Street",
                    ConnectorCount = 1,
                    PowerKW = 22,
                },
                new EVCharging.Domain.Charging.ChargingStation
                {
                    Id = denied,
                    Code = "DENY",
                    Name = "Denied",
                    City = "City",
                    Address = "Street",
                    ConnectorCount = 1,
                    PowerKW = 22,
                }
            );
            db.AdministratorGrants.Add(
                new() { AdministratorId = auth.User.Id, StationId = allowed }
            );
            await db.SaveChangesAsync();
        }
        http.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        using var stream = await http.GetAsync(
            "/api/v1/admin/events",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );
        stream.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
            if (line == "")
                break;
        var bus = factory.Services.GetRequiredService<StationEventBus>();
        bus.Publish(
            new(
                denied,
                1,
                "Changed",
                Guid.NewGuid(),
                EventId: Guid.NewGuid(),
                EventType: "DeniedPrivate"
            )
        );
        bus.Publish(
            new(
                allowed,
                2,
                "Changed",
                Guid.NewGuid(),
                EventId: Guid.NewGuid(),
                EventType: "SessionMeasured",
                TelemetryOnly: true
            )
        );
        string? data = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
            if (line.StartsWith("data: "))
            {
                data = line[6..];
                break;
            }
        Assert.NotNull(data);
        var notice = JsonDocument.Parse(data!).RootElement;
        Assert.Equal(allowed, notice.GetProperty("stationId").GetGuid());
        Assert.Equal("SessionMeasured", notice.GetProperty("eventType").GetString());
        Assert.True(notice.GetProperty("telemetryOnly").GetBoolean());
        Assert.False(notice.TryGetProperty("session", out _));
        Assert.False(notice.TryGetProperty("recipientAccountId", out _));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            db.AdministratorGrants.RemoveRange(await db.AdministratorGrants.ToListAsync());
            (await db.Accounts.SingleAsync(a => a.Id == auth.User.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        bus.Publish(
            new(allowed, 3, "Changed", EventId: Guid.NewGuid(), EventType: "RevokedPrivate")
        );
        Assert.DoesNotContain("RevokedPrivate", await reader.ReadToEndAsync(timeout.Token));
    }
}

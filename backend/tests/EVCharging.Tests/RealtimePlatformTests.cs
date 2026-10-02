using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class RealtimePlatformTests
{
    [Fact]
    public async Task SupersededCloseCannotDisconnectTheCurrentSocketAndStartupClosesAbandonedRows()
    {
        await using var factory = new ApiFactory(simulator: true);
        using var client = factory.CreateClient();
        using var firstScope = factory.Services.CreateScope();
        var db = firstScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var device = await db.OcppChargePoints.SingleAsync(x => x.Identity == "SIM-001");
        var old = await firstScope
            .ServiceProvider.GetRequiredService<IOcppGateway>()
            .OpenAsync(device.Id, default);
        using (var scope = factory.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IOcppGateway>()
                .OpenAsync(device.Id, default);
        using (var scope = factory.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IOcppGateway>()
                .CloseAsync(device.Id, old, 1000, default);
        db.ChangeTracker.Clear();
        Assert.Single(
            await db
                .OcppConnections.Where(x =>
                    x.ChargePointId == device.Id && x.DisconnectedAt == null
                )
                .ToListAsync()
        );
        Assert.True(
            (
                await db.ChargingConnectors.SingleAsync(x =>
                    x.Id
                    == db.OcppConnectors.Where(c => c.ChargePointId == device.Id)
                        .Select(c => c.BusinessConnectorId)
                        .First()
                )
            ).IsOnline
        );
        // Startup reconciliation is platform behavior, even with simulator mode disabled.
        factory.Services.GetRequiredService<IOptions<OcppOptions>>().Value.SimulatorEnabled = false;
        using (var scope = factory.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<OcppSimulatorProvisioner>()
                .InitializeAsync();
        db.ChangeTracker.Clear();
        Assert.Empty(await db.OcppConnections.Where(x => x.DisconnectedAt == null).ToListAsync());
        Assert.False(await db.ChargingConnectors.AnyAsync(x => x.IsOnline));
    }

    [Fact]
    public async Task LivenessDetectionContinuesWithSimulatorDisabled()
    {
        await using var factory = new ApiFactory(simulator: true);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        factory.Services.GetRequiredService<IOptions<OcppOptions>>().Value.SimulatorEnabled = false;
        Guid connectorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = await db.OcppChargePoints.SingleAsync(x => x.Identity == "SIM-001");
            device.LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            connectorId = (await db.OcppConnectors.SingleAsync(x => x.ChargePointId == device.Id))
                .BusinessConnectorId!
                .Value;
            (await db.ChargingConnectors.SingleAsync(x => x.Id == connectorId)).IsOnline = true;
            await db.SaveChangesAsync();
        }
        while (true)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            if (
                !await db.ChargingConnectors.AnyAsync(
                    x => x.Id == connectorId && x.IsOnline,
                    timeout.Token
                )
            )
                break;
            await Task.Delay(100, timeout.Token);
        }
    }

    [Fact]
    public async Task SlowSubscribersReconnectInsteadOfSilentlyDroppingLifecycleEvents()
    {
        var bus = new StationEventBus();
        var owner = Guid.NewGuid();
        var point = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = bus.Subscribe(owner, timeout.Token).GetAsyncEnumerator();
        var pending = reader.MoveNextAsync().AsTask();
        bus.Publish(new(point, 0, "Private", Guid.NewGuid())); // Another account is filtered.
        Assert.False(pending.IsCompleted);
        bus.Publish(new(point, 1, "Started", owner));
        Assert.True(await pending);
        Assert.Equal("Started", reader.Current.Status);
        for (var i = 0; i < 256; i++)
            bus.Publish(new(point, i + 2, i == 255 ? "Completed" : "Measured", owner));
        bus.Publish(new(point, 258, "Overflow", owner));
        for (var i = 0; i < 256; i++)
        {
            Assert.True(await reader.MoveNextAsync());
            Assert.Equal(i + 2, reader.Current.Version);
        }
        Assert.Equal("Completed", reader.Current.Status);
        Assert.False(await reader.MoveNextAsync()); // SSE ends; reconnect recovers the latest database state.
    }

    [Fact]
    public async Task CommittedOutboxDispatchWorksWithSimulatorDisabledAndCarriesStableEnvelope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = factory.Services.GetRequiredService<IStationEventStream>();
        await using var reader = stream
            .Subscribe(Guid.NewGuid(), timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var next = reader.MoveNextAsync().AsTask();
        var station = Guid.NewGuid();
        var entry = new RealtimeOutboxEvent
        {
            EventType = "StationChanged",
            EntityType = "ChargingStation",
            EntityId = station,
            EntityVersion = 7,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new StationChanged(station, 7, "Charging")),
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            db.RealtimeOutbox.Add(entry);
            await db.SaveChangesAsync();
        }
        factory.Services.GetRequiredService<StationEventSignal>().Notify();
        Assert.True(await next);
        Assert.Equal(entry.Id, reader.Current.EventId);
        Assert.Equal(7, reader.Current.EntityVersion);
        Assert.Equal(station, reader.Current.StationId);
        Assert.Null(reader.Current.RecipientAccountId);
    }

    [Fact]
    public void ModelContainsClaimInvariantsAndMatchesTheMigrationSnapshot()
    {
        using var db = new ChargingDbContext(
            new DbContextOptionsBuilder<ChargingDbContext>()
                .UseNpgsql("Host=localhost;Database=unused_model_only;Username=unused")
                .Options
        );
        Assert.False(db.Database.HasPendingModelChanges()); // Does not connect to PostgreSQL.
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var claim = model.FindEntityType(
            typeof(EVCharging.Domain.Charging.ChargingConnectionRequest)
        )!;
        foreach (var name in new[] { "ux_claim_active_driver", "ux_claim_active_connector" })
        {
            var index = Assert.Single(claim.GetIndexes(), x => x.GetDatabaseName() == name);
            Assert.True(index.IsUnique);
            Assert.Equal("state IN ('Waiting', 'Linked')", index.GetFilter());
        }
    }
}

using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class RealtimePlatformTests
{
    [Fact]
    public async Task CommittedOutboxDispatchWorksWithSimulatorDisabledAndCarriesStableEnvelope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stream = factory.Services.GetRequiredService<IStationEventStream>();
        await using var reader = stream.Subscribe(Guid.NewGuid(), timeout.Token).GetAsyncEnumerator(timeout.Token);
        var next = reader.MoveNextAsync().AsTask();
        var station = Guid.NewGuid();
        var entry = new RealtimeOutboxEvent
        {
            EventType = "StationChanged", EntityType = "ChargingStation", EntityId = station,
            EntityVersion = 7, CreatedAt = DateTimeOffset.UtcNow, AvailableAt = DateTimeOffset.UtcNow,
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
        using var db = new ChargingDbContext(new DbContextOptionsBuilder<ChargingDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_model_only;Username=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges()); // Does not connect to PostgreSQL.
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var claim = model.FindEntityType(typeof(EVCharging.Domain.Charging.ChargingConnectionRequest))!;
        foreach (var name in new[] { "ux_claim_active_driver", "ux_claim_active_connector" })
        {
            var index = Assert.Single(claim.GetIndexes(), x => x.GetDatabaseName() == name);
            Assert.True(index.IsUnique);
            Assert.Equal("state IN ('Waiting', 'Linked')", index.GetFilter());
        }
    }
}

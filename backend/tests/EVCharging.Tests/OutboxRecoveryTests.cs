using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OutboxRecoveryTests
{
    [Theory]
    [InlineData("null", "PublicStation")]
    [InlineData("{broken", "PublicStation")]
    [InlineData("{\"StationId\":\"14eedf70-4774-4b4d-808e-8f91dbbc13ce\"}", "Driver")]
    [InlineData(
        "{\"StationId\":\"14eedf70-4774-4b4d-808e-8f91dbbc13ce\",\"Command\":{}}",
        "PublicStation"
    )]
    public async Task InvalidEventIsRetainedWithoutBlockingLaterCommittedChanges(
        string payload,
        string audience
    )
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var recipient = Guid.NewGuid();
        var station = Guid.NewGuid();
        var stream = factory.Services.GetRequiredService<IStationEventStream>();
        await using var reader = stream
            .Subscribe(recipient, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var next = reader.MoveNextAsync().AsTask();
        var now = DateTimeOffset.UtcNow;
        var invalid = new RealtimeOutboxEvent
        {
            EntityId = station,
            Audience = audience,
            PayloadJson = payload,
            CreatedAt = now.AddSeconds(-1),
            AvailableAt = now,
            EventType = "StationChanged",
            EntityType = "ChargingStation",
        };
        var valid = new RealtimeOutboxEvent
        {
            EntityId = station,
            PayloadJson = JsonSerializer.Serialize(new StationChanged(station, 2, "Charging")),
            CreatedAt = now,
            AvailableAt = now,
            EventType = "StationChanged",
            EntityType = "ChargingStation",
            EntityVersion = 2,
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            db.RealtimeOutbox.AddRange(invalid, valid);
            await db.SaveChangesAsync();
        }
        factory.Services.GetRequiredService<StationEventSignal>().Notify();
        Assert.True(await next);
        Assert.Equal(valid.Id, reader.Current.EventId);
        // Publication precedes the marker save; wait for its committed retry state.
        while (true)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var failed = await db.RealtimeOutbox.SingleAsync(
                x => x.Id == invalid.Id,
                timeout.Token
            );
            if (failed.LastFailureCode is not null)
            {
                Assert.Equal("invalid_event", failed.LastFailureCode);
                Assert.Equal(1, failed.Attempts);
                Assert.Null(failed.PublishedAt);
                Assert.True(failed.AvailableAt > now.AddMinutes(4));
                break;
            }
            await Task.Delay(20, timeout.Token);
        }
    }
}

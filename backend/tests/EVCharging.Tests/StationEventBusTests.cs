using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;

namespace EVCharging.Tests;

public sealed class StationEventBusTests
{
    [Fact]
    public async Task PrivateChargingEventsNeverReachAnotherDriver()
    {
        var bus = new StationEventBus();
        var owner = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var ownerEvents = bus.Subscribe(owner, timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        await using var otherEvents = bus.Subscribe(Guid.NewGuid(), timeout.Token)
            .GetAsyncEnumerator(timeout.Token);
        var ownerNext = ownerEvents.MoveNextAsync().AsTask();
        var otherNext = otherEvents.MoveNextAsync().AsTask();
        var station = Guid.NewGuid();
        bus.Publish(new StationChanged(station, 1, "Charging", owner));
        Assert.True(await ownerNext);
        Assert.Equal(owner, ownerEvents.Current.RecipientAccountId);
        bus.Publish(new StationChanged(station, 2, "Available"));
        Assert.True(await otherNext);
        Assert.Null(otherEvents.Current.RecipientAccountId);
        Assert.Equal(2, otherEvents.Current.Version);
    }
}

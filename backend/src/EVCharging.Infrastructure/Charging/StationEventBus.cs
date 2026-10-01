using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EVCharging.Application.Charging;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationEventBus : IStationEventStream
{
    private readonly ConcurrentDictionary<
        Guid,
        (Guid AccountId, Channel<StationChanged> Channel)
    > listeners = new();

    public void Publish(StationChanged change)
    {
        foreach (var listener in listeners.Values)
            if (
                change.RecipientAccountId is null
                || change.RecipientAccountId == listener.AccountId
            )
                listener.Channel.Writer.TryWrite(change);
    }

    public async IAsyncEnumerable<StationChanged> Subscribe(
        Guid accountId,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<StationChanged>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest }
        );
        listeners[id] = (accountId, channel);
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(ct))
                yield return change;
        }
        finally
        {
            listeners.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}

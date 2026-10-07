using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EVCharging.Application.Charging;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationEventBus : IStationEventStream
{
    private readonly ConcurrentDictionary<
        Guid,
        (Guid AccountId, bool Operations, Channel<StationChanged> Channel)
    > listeners = new();

    public void Publish(StationChanged change, bool operationsOnly = false)
    {
        foreach (var listener in listeners.Values)
            if (
                listener.Operations
                || !operationsOnly
                    && (
                        change.RecipientAccountId is null
                        || change.RecipientAccountId == listener.AccountId
                    )
            )
                if (
                    !listener.Channel.Writer.TryWrite(
                        listener.Operations
                            ? new StationChanged(
                                change.StationId,
                                change.Version,
                                "Changed",
                                EventId: change.EventId,
                                EventType: change.EventType,
                                EntityType: change.EntityType,
                                EntityId: change.EntityId,
                                EntityVersion: change.EntityVersion,
                                OccurredAt: change.OccurredAt,
                                TelemetryOnly: change.TelemetryOnly
                            )
                            : change
                    )
                )
                    listener.Channel.Writer.TryComplete(); // Reconnect + snapshot on overflow.
    }

    public IAsyncEnumerable<StationChanged> Subscribe(Guid accountId, CancellationToken ct) =>
        SubscribeCore(accountId, false, ct);

    public IAsyncEnumerable<StationChanged> SubscribeOperations(
        Guid accountId,
        CancellationToken ct
    ) => SubscribeCore(accountId, true, ct);

    private async IAsyncEnumerable<StationChanged> SubscribeCore(
        Guid accountId,
        bool operations,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<StationChanged>(
            new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait }
        );
        listeners[id] = (accountId, operations, channel);
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

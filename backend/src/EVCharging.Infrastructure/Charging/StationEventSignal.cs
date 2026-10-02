using System.Threading.Channels;

namespace EVCharging.Infrastructure.Charging;

// One wake-up is enough: the worker drains the committed outbox, not this channel.
// Call Notify only after the surrounding database transaction has committed.
public sealed class StationEventSignal
{
    private readonly Channel<bool> pending = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    public void Notify() => pending.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken ct)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wake.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await pending.Reader.ReadAsync(wake.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

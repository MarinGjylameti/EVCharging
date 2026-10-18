using System.Collections.Concurrent;

namespace EVCharging.Api.Ocpp;

// The demo runs in one API instance. PostgreSQL serializes durable updates per device.
public sealed class OcppConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, Registration> connections = new();

    public IDisposable? TryOpen(Guid deviceId)
    {
        var registration = new Registration(connections, deviceId);
        return connections.TryAdd(deviceId, registration) ? registration : null;
    }

    private sealed class Registration(
        ConcurrentDictionary<Guid, Registration> connections,
        Guid deviceId
    ) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                // Remove only this owner, even if cleanup is repeated after a reconnect.
                ((ICollection<KeyValuePair<Guid, Registration>>)connections).Remove(
                    new(deviceId, this)
                );
        }
    }
}

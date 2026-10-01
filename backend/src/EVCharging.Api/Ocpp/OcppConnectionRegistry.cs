using System.Collections.Concurrent;

namespace EVCharging.Api.Ocpp;

// The demo runs in one API instance. PostgreSQL serializes durable updates per device.
public sealed class OcppConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, byte> connections = new();

    public bool TryOpen(Guid deviceId) => connections.TryAdd(deviceId, 0);

    public void Close(Guid deviceId) => connections.TryRemove(deviceId, out _);
}

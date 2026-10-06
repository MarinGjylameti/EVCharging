using EVCharging.Application.Charging;

namespace EVCharging.Infrastructure.Charging;

// A bounded diagnostic buffer for the current process; charging history stays in the database.
public sealed class ApplicationDiagnostics(TimeProvider clock) : IApplicationDiagnostics
{
    private const int Capacity = 1000;
    private readonly object gate = new();
    private readonly Queue<ApplicationLogEntry> entries = new();
    private readonly string instanceId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset startedAt = clock.GetUtcNow();
    private long sequence;

    public void Record(string source, string level, string message, string? identity = null)
    {
        lock (gate)
        {
            entries.Enqueue(
                new(
                    ++sequence,
                    clock.GetUtcNow(),
                    source,
                    level,
                    Clean(message, 2000),
                    identity is null ? null : Clean(identity, 40)
                )
            );
            while (entries.Count > Capacity)
                entries.Dequeue();
        }
    }

    public ApplicationLogPage Read(string? source = null, string? identity = null)
    {
        lock (gate)
            return new(
                instanceId,
                startedAt,
                Capacity,
                entries
                    .Where(x =>
                        (source is null || x.Source == source)
                        && (
                            identity is null
                            || x.Identity?.Contains(identity, StringComparison.OrdinalIgnoreCase)
                                == true
                        )
                    )
                    .Reverse()
                    .Take(200)
                    .ToArray()
            );
    }

    private static string Clean(string value, int limit) =>
        new(value.Take(limit).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}

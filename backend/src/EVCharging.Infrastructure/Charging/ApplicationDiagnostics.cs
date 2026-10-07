using System.Text.Json;
using EVCharging.Application.Charging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

// A small live view plus a bounded write queue. Every event is also emitted to stdout.
// Database failures must never block the charger transport or erase the queued batch.
public sealed class ApplicationDiagnostics(
    TimeProvider clock,
    IOptions<DiagnosticOptions>? options = null,
    ILogger<ApplicationDiagnostics>? output = null
) : IApplicationDiagnostics
{
    private const int Capacity = 1000;
    private readonly object gate = new();
    private readonly Queue<ApplicationLogEntry> entries = new();
    private readonly Queue<ApplicationLogEntry> pending = new();
    private readonly AsyncLocal<ApplicationLogContext?> current = new();
    private readonly int queueCapacity = options?.Value.QueueCapacity ?? 10_000;
    private readonly string instanceId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset startedAt = clock.GetUtcNow();
    private long sequence;
    private long dropped;
    private DateTimeOffset? lastPersistedAt;
    private string? failureCode;

    public void Record(
        string source,
        string level,
        string message,
        string? identity = null,
        ApplicationLogContext? context = null
    )
    {
        ApplicationLogEntry entry;
        var overflow = false;
        lock (gate)
        {
            entry = new(
                ++sequence,
                clock.GetUtcNow(),
                Clean(source, 30),
                Clean(level, 20),
                Clean(message, 2000),
                identity is null ? null : Clean(identity, 40),
                Guid.NewGuid(),
                instanceId,
                Sanitize(Merge(current.Value, context))
            );
            entries.Enqueue(entry);
            while (entries.Count > Capacity)
                entries.Dequeue();
            if (pending.Count < queueCapacity)
                pending.Enqueue(entry);
            else
            {
                dropped++;
                overflow = dropped == 1 || dropped % 100 == 0;
            }
        }
        // This category is excluded from the capturing provider, preventing recursion.
        output?.Log(
            Enum.TryParse<LogLevel>(level, out var severity) ? severity : LogLevel.Information,
            "DiagnosticEvent {DiagnosticEvent}",
            JsonSerializer.Serialize(entry)
        );
        if (overflow)
            output?.LogError(
                "Diagnostic persistence queue is full. Events continue in stdout; dropped from database queue: {Dropped}.",
                StorageStatus().Dropped
            );
    }

    public IDisposable BeginScope(ApplicationLogContext context)
    {
        var previous = current.Value;
        current.Value = Merge(previous, context);
        return new Scope(() => current.Value = previous);
    }

    public ApplicationLogStorageStatus StorageStatus()
    {
        lock (gate)
            return new(pending.Count, dropped, lastPersistedAt, failureCode);
    }

    public IReadOnlyList<ApplicationLogEntry> PendingBatch(int limit = 200)
    {
        lock (gate)
            return pending.Take(limit).ToArray();
    }

    public void Acknowledge(IReadOnlyList<ApplicationLogEntry> batch)
    {
        lock (gate)
        {
            foreach (var entry in batch)
            {
                if (pending.TryPeek(out var first) && first.EventId == entry.EventId)
                    pending.Dequeue();
            }
            lastPersistedAt = clock.GetUtcNow();
            failureCode = null;
        }
    }

    public void PersistenceFailed(string code)
    {
        lock (gate)
            failureCode = Clean(code, 100);
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

    private static ApplicationLogContext Merge(
        ApplicationLogContext? parent,
        ApplicationLogContext? child
    ) =>
        new(
            child?.TraceId ?? parent?.TraceId,
            child?.ConnectionId ?? parent?.ConnectionId,
            child?.MessageId ?? parent?.MessageId,
            child?.Action ?? parent?.Action,
            child?.EventName ?? parent?.EventName,
            child?.ErrorType ?? parent?.ErrorType,
            child?.ErrorCode ?? parent?.ErrorCode,
            child?.DurationMs ?? parent?.DurationMs,
            child?.HttpStatus ?? parent?.HttpStatus,
            child?.StackTrace ?? parent?.StackTrace,
            child?.SessionId ?? parent?.SessionId,
            child?.StationId ?? parent?.StationId,
            child?.ConnectorId ?? parent?.ConnectorId,
            child?.ConnectorNumber ?? parent?.ConnectorNumber,
            child?.InboxMessageId ?? parent?.InboxMessageId
        );

    private static ApplicationLogContext Sanitize(ApplicationLogContext value) =>
        value with
        {
            TraceId = value.TraceId is null ? null : Clean(value.TraceId, 100),
            MessageId = value.MessageId is null ? null : Clean(value.MessageId, 100),
            Action = value.Action is null ? null : Clean(value.Action, 60),
            EventName = value.EventName is null ? null : Clean(value.EventName, 100),
            ErrorType = value.ErrorType is null ? null : Clean(value.ErrorType, 100),
            ErrorCode = value.ErrorCode is null ? null : Clean(value.ErrorCode, 100),
            StackTrace = value.StackTrace is null ? null : Clean(value.StackTrace, 8000),
        };

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

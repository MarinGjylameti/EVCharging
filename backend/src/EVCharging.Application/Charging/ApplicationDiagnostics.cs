namespace EVCharging.Application.Charging;

public sealed record ApplicationLogEntry(
    long Sequence,
    DateTimeOffset RecordedAt,
    string Source,
    string Level,
    string Message,
    string? Identity,
    Guid EventId = default,
    string? InstanceId = null,
    ApplicationLogContext? Context = null,
    long? StorageId = null
);

public sealed record ApplicationLogContext(
    string? TraceId = null,
    Guid? ConnectionId = null,
    string? MessageId = null,
    string? Action = null,
    string? EventName = null,
    string? ErrorType = null,
    string? ErrorCode = null,
    long? DurationMs = null,
    int? HttpStatus = null,
    string? StackTrace = null,
    Guid? SessionId = null,
    Guid? StationId = null,
    Guid? ConnectorId = null,
    int? ConnectorNumber = null,
    Guid? InboxMessageId = null
);

public sealed record ApplicationLogFilter(
    string? Source = null,
    string? Identity = null,
    string? Level = null,
    string? TraceId = null,
    Guid? ConnectionId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Until = null,
    long? BeforeId = null,
    int Limit = 200
);

public sealed record ApplicationLogStorageStatus(
    int Pending,
    long Dropped,
    DateTimeOffset? LastPersistedAt,
    string? FailureCode
);

public sealed record ApplicationLogHistory(
    IReadOnlyList<ApplicationLogEntry> Entries,
    long? NextBeforeId,
    int RetentionDays,
    ApplicationLogStorageStatus Storage
);

public interface IApplicationLogHistory
{
    Task<ApplicationLogHistory> ReadAsync(ApplicationLogFilter filter, CancellationToken ct);
}

public sealed record ApplicationLogPage(
    string InstanceId,
    DateTimeOffset StartedAt,
    int Capacity,
    IReadOnlyList<ApplicationLogEntry> Entries
);

public interface IApplicationDiagnostics
{
    void Record(
        string source,
        string level,
        string message,
        string? identity = null,
        ApplicationLogContext? context = null
    );
    IDisposable BeginScope(ApplicationLogContext context);
    ApplicationLogPage Read(string? source = null, string? identity = null);
}

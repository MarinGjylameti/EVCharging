namespace EVCharging.Application.Charging;

public sealed record ApplicationLogEntry(
    long Sequence,
    DateTimeOffset RecordedAt,
    string Source,
    string Level,
    string Message,
    string? Identity
);

public sealed record ApplicationLogPage(
    string InstanceId,
    DateTimeOffset StartedAt,
    int Capacity,
    IReadOnlyList<ApplicationLogEntry> Entries
);

public interface IApplicationDiagnostics
{
    void Record(string source, string level, string message, string? identity = null);
    ApplicationLogPage Read(string? source = null, string? identity = null);
}

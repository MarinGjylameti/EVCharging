namespace EVCharging.Infrastructure.Persistence;

public sealed class ApplicationLogRecord
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public string InstanceId { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Source { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Identity { get; set; }
    public string? TraceId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string ContextJson { get; set; } = "{}";
}

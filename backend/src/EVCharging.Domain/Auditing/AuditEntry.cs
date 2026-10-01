namespace EVCharging.Domain.Auditing;

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid EntityId { get; set; }
    public string? ChangesJson { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

namespace EVCharging.Infrastructure.Charging;

public sealed class DiagnosticOptions
{
    public int RetentionDays { get; set; } = 30;
    public int QueueCapacity { get; set; } = 10_000;
    public int FlushSeconds { get; set; } = 2;
}

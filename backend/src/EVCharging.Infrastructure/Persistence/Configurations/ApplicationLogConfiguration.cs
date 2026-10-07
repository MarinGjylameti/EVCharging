using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationLogConfiguration : IEntityTypeConfiguration<ApplicationLogRecord>
{
    public void Configure(EntityTypeBuilder<ApplicationLogRecord> e)
    {
        e.ToTable("application_logs");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.EventId).IsUnique();
        e.Property(x => x.InstanceId).HasMaxLength(32);
        e.Property(x => x.Source).HasMaxLength(30);
        e.Property(x => x.Level).HasMaxLength(20);
        e.Property(x => x.Message).HasMaxLength(2000);
        e.Property(x => x.Identity).HasMaxLength(40);
        e.Property(x => x.TraceId).HasMaxLength(100);
        e.Property(x => x.ContextJson).HasColumnType("jsonb");
        e.HasIndex(x => x.RecordedAt);
        e.HasIndex(x => new { x.Source, x.Id });
        e.HasIndex(x => new { x.Level, x.Id });
        e.HasIndex(x => new { x.Identity, x.Id });
        e.HasIndex(x => new { x.TraceId, x.Id });
        e.HasIndex(x => new { x.ConnectionId, x.Id });
    }
}

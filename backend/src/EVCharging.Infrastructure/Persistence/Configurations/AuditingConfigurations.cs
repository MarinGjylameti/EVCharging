using EVCharging.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> e)
    {
        e.ToTable("audit_entries");
        e.HasKey(x => x.Id);
        e.Property(x => x.ActorRole).HasMaxLength(20);
        e.Property(x => x.Action).HasMaxLength(80);
        e.HasIndex(x => new { x.ActorId, x.RecordedAt });
    }
}

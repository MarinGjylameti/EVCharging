using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class RealtimeOutboxEventConfiguration
    : IEntityTypeConfiguration<RealtimeOutboxEvent>
{
    public void Configure(EntityTypeBuilder<RealtimeOutboxEvent> e)
    {
        e.ToTable(
            "realtime_outbox",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_realtime_outbox_version_attempts",
                    "entity_version >= 0 AND attempts >= 0"
                );
                table.HasCheckConstraint(
                    "ck_realtime_outbox_audience",
                    "audience IN ('PublicStation', 'Driver', 'Admin', 'Operations') AND (audience <> 'Driver' OR recipient_account_id IS NOT NULL)"
                );
            }
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.EventType).HasMaxLength(60);
        e.Property(x => x.EntityType).HasMaxLength(60);
        e.Property(x => x.Audience).HasMaxLength(20);
        e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        e.Property(x => x.LastFailureCode).HasMaxLength(80);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.RecipientAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.PublishedAt, x.AvailableAt }).HasFilter("published_at IS NULL");
        e.HasIndex(x => new
        {
            x.EntityType,
            x.EntityId,
            x.EntityVersion,
        });
        e.HasIndex(x => new { x.RecipientAccountId, x.CreatedAt });
    }
}

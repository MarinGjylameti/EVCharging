using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> e)
    {
        e.ToTable("notifications");
        e.HasKey(x => x.Id);
        e.Property(x => x.SourceEventId).HasMaxLength(200);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
        e.HasIndex(x => new
            {
                x.UserId,
                x.SourceEventId,
                x.Kind,
            })
            .IsUnique();
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PushDeliveryConfiguration : IEntityTypeConfiguration<PushDelivery>
{
    public void Configure(EntityTypeBuilder<PushDelivery> e)
    {
        e.ToTable("push_deliveries");
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.NotificationId, x.DeviceId }).IsUnique();
        e.HasIndex(x => new { x.CompletedAt, x.NextAttemptAt });
        e.Property(x => x.TicketId).HasMaxLength(128);
        e.Property(x => x.FailureCode).HasMaxLength(80);
        e.HasOne(x => x.Notification)
            .WithMany()
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<DeviceRegistration>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

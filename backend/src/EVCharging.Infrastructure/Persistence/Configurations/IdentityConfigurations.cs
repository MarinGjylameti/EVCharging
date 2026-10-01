using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> e)
    {
        e.ToTable(
            "accounts",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_account_role",
                    "role IN ('Driver', 'Admin', 'Superadmin')"
                );
                table.HasCheckConstraint("ck_account_language", "language IN ('Sq', 'En')");
            }
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Email).HasMaxLength(254);
        e.Property(x => x.NormalizedEmail).HasMaxLength(254);
        e.HasIndex(x => x.NormalizedEmail).IsUnique();
        e.Property(x => x.PasswordHash).HasMaxLength(500);
        e.Property(x => x.FirstName).HasMaxLength(80);
        e.Property(x => x.LastName).HasMaxLength(80);
        e.Property(x => x.Phone).HasMaxLength(32);
        e.Property(x => x.VehicleMake).HasMaxLength(80);
        e.Property(x => x.VehicleModel).HasMaxLength(80);
        e.Property(x => x.BatteryType).HasMaxLength(80);
        e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Language).HasConversion<string>().HasMaxLength(2);
        e.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class LoginSessionConfiguration : IEntityTypeConfiguration<LoginSession>
{
    public void Configure(EntityTypeBuilder<LoginSession> e)
    {
        e.ToTable("login_sessions");
        e.HasKey(x => x.Id);
        e.Property(x => x.TokenHash).HasMaxLength(64);
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ExpiresAt });
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> e)
    {
        e.ToTable("device_registrations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Token).HasMaxLength(256);
        e.Property(x => x.Platform).HasMaxLength(10);
        e.HasIndex(x => x.Token).IsUnique();
        e.HasIndex(x => x.UserId);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<LoginSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

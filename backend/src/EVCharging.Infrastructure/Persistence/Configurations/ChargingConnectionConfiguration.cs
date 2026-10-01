using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class ChargingConnectionConfiguration
    : IEntityTypeConfiguration<ChargingConnectionRequest>
{
    public void Configure(EntityTypeBuilder<ChargingConnectionRequest> e)
    {
        e.ToTable(
            "charging_connection_requests",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_connection_state",
                    "state IN ('Waiting', 'Linked', 'Completed', 'Cancelled', 'Expired')"
                );
                table.HasCheckConstraint(
                    "ck_connection_time",
                    "expires_at > created_at AND connector_number > 0"
                );
                table.HasCheckConstraint(
                    "ck_connection_link",
                    "(state IN ('Linked', 'Completed')) = (session_id IS NOT NULL AND linked_at IS NOT NULL)"
                );
            }
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.State).HasMaxLength(20);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppChargingTransaction>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StationId, x.ConnectorNumber })
            .IsUnique()
            .HasFilter("state = 'Waiting'");
        e.HasIndex(x => x.SessionId).IsUnique().HasFilter("session_id IS NOT NULL");
        e.HasIndex(x => new { x.DriverId, x.CreatedAt });
    }
}

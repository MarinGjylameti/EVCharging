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
        e.HasOne<ChargingSession>()
            .WithMany()
            .HasForeignKey(x => new { x.SessionId, x.StationId, x.ConnectorNumber })
            .HasPrincipalKey(x => new { x.Id, x.PointId, x.ConnectorNumber })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.StationId, x.ConnectorNumber })
            .IsUnique()
            .HasDatabaseName("ux_claim_active_connector")
            .HasFilter("state IN ('Waiting', 'Linked')");
        e.HasIndex(x => x.DriverId).IsUnique().HasDatabaseName("ux_claim_active_driver")
            .HasFilter("state IN ('Waiting', 'Linked')");
        e.HasIndex(x => x.SessionId).IsUnique().HasFilter("session_id IS NOT NULL");
        e.HasIndex(x => new { x.DriverId, x.CreatedAt });
    }
}

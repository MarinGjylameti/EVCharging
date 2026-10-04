using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class ChargingLocationConfiguration : IEntityTypeConfiguration<ChargingLocation>
{
    public void Configure(EntityTypeBuilder<ChargingLocation> e)
    {
        e.ToTable(
            "charging_locations",
            t =>
                t.HasCheckConstraint(
                    "ck_location_coordinates",
                    "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180"
                )
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(120);
        e.Property(x => x.City).HasMaxLength(80);
        e.Property(x => x.Address).HasMaxLength(200);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => new { x.IsArchived, x.Name });
    }
}

internal sealed class AdministratorGrantConfiguration : IEntityTypeConfiguration<AdministratorGrant>
{
    public void Configure(EntityTypeBuilder<AdministratorGrant> e)
    {
        e.ToTable(
            "administrator_grants",
            t =>
                t.HasCheckConstraint(
                    "ck_grant_one_resource",
                    "(location_id IS NULL) <> (station_id IS NULL)"
                )
        );
        e.HasKey(x => x.Id);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AdministratorId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingLocation>()
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.AdministratorId, x.LocationId })
            .IsUnique()
            .HasFilter("location_id IS NOT NULL");
        e.HasIndex(x => new { x.AdministratorId, x.StationId })
            .IsUnique()
            .HasFilter("station_id IS NOT NULL");
    }
}

internal sealed class PriceRevisionConfiguration : IEntityTypeConfiguration<PriceRevision>
{
    public void Configure(EntityTypeBuilder<PriceRevision> e)
    {
        e.ToTable(
            "price_revisions",
            t =>
                t.HasCheckConstraint(
                    "ck_revision_price",
                    "price_per_kwh >= 0 AND (previous_price IS NULL OR previous_price >= 0)"
                )
        );
        e.HasKey(x => x.Id);
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.PreviousPrice).HasPrecision(10, 4);
        e.Property(x => x.PricePerKWh).HasPrecision(10, 4);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.HasIndex(x => new { x.StationId, x.RecordedAt });
    }
}

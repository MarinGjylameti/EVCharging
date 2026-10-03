using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class ChargingStationConfiguration : IEntityTypeConfiguration<ChargingStation>
{
    public void Configure(EntityTypeBuilder<ChargingStation> e)
    {
        e.ToTable(
            "charging_stations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_station_coordinates",
                    "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180"
                );
                table.HasCheckConstraint(
                    "ck_station_capacity",
                    "connector_count > 0 AND power_kw > 0 AND price_per_kwh >= 0"
                );
                table.HasCheckConstraint(
                    "ck_station_manual_restriction",
                    "manual_restriction IS NULL OR manual_restriction IN ('OutOfService', 'Maintenance')"
                );
            }
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(40);
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Name).HasMaxLength(120);
        e.Property(x => x.City).HasMaxLength(80);
        e.Property(x => x.Address).HasMaxLength(200);
        e.Property(x => x.ConnectorType).HasMaxLength(40);
        e.Property(x => x.PowerKW).HasPrecision(10, 2);
        e.Property(x => x.PricePerKWh).HasPrecision(10, 4);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.ManualRestriction).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.StatusSource).HasMaxLength(40);
        e.Property(x => x.WarningCode).HasMaxLength(50);
        e.Property(x => x.ForecastSource).HasMaxLength(20);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => new { x.City, x.IsArchived });
    }
}

internal sealed class ChargingHistoryConfiguration : IEntityTypeConfiguration<ChargingHistory>
{
    public void Configure(EntityTypeBuilder<ChargingHistory> e)
    {
        e.ToTable(
            "charging_history",
            table =>
                table.HasCheckConstraint(
                    "ck_history_measurement",
                    "energy_kwh >= 0 AND unit_price_snapshot >= 0 AND total_cost >= 0 AND ended_at >= started_at"
                )
        );
        e.HasKey(x => x.Id);
        e.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.EnergyKWh).HasPrecision(12, 3);
        e.Property(x => x.UnitPriceSnapshot).HasPrecision(10, 4);
        e.Property(x => x.TotalCost).HasPrecision(12, 2);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.HasIndex(x => new { x.DriverId, x.StartedAt });
    }
}

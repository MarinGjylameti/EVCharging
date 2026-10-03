using EVCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class ChargingConnectorConfiguration : IEntityTypeConfiguration<ChargingConnector>
{
    public void Configure(EntityTypeBuilder<ChargingConnector> e)
    {
        e.ToTable(
            "charging_connectors",
            t => t.HasCheckConstraint("ck_charging_connector_number", "number > 0")
        );
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.StationId, x.Number }).IsUnique();
        e.HasAlternateKey(x => new
        {
            x.Id,
            x.StationId,
            x.Number,
        });
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        e.Property(x => x.FaultCode).HasMaxLength(50);
        e.Property(x => x.FaultInfo).HasMaxLength(80);
        e.Property(x => x.DeviceStatus).HasMaxLength(30);
        e.Property(x => x.DeviceFaultCode).HasMaxLength(50);
        e.Property(x => x.DeviceFaultInfo).HasMaxLength(80);
        e.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> e)
    {
        e.ToTable(
            "charging_sessions",
            t =>
            {
                t.HasCheckConstraint(
                    "ck_session_time",
                    "ended_at IS NULL OR ended_at >= started_at"
                );
                t.HasCheckConstraint(
                    "ck_session_values",
                    "(energy_kwh IS NULL OR energy_kwh >= 0) AND unit_price_snapshot >= 0 AND (state_of_charge IS NULL OR state_of_charge BETWEEN 0 AND 100)"
                );
                t.HasCheckConstraint(
                    "ck_session_battery_capacity",
                    "battery_capacity_kwh IS NULL OR battery_capacity_kwh > 0 AND battery_capacity_kwh <= 1000"
                );
            }
        );
        e.HasKey(x => x.Id);
        e.HasAlternateKey(x => new
        {
            x.Id,
            x.PointId,
            x.ConnectorNumber,
        });
        e.HasOne<ChargingConnector>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.ConnectorId,
                x.PointId,
                x.ConnectorNumber,
            })
            .HasPrincipalKey(x => new
            {
                x.Id,
                x.StationId,
                x.Number,
            })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.ConnectorId).IsUnique().HasFilter("ended_at IS NULL");
        e.HasIndex(x => new { x.PointId, x.StartReceivedAt });
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.Property(x => x.EnergyKWh).HasPrecision(18, 6);
        e.Property(x => x.PowerKW).HasPrecision(12, 3);
        e.Property(x => x.StateOfCharge).HasPrecision(6, 3);
        e.Property(x => x.BatteryCapacityKWh).HasPrecision(6, 2);
        e.Property(x => x.TemperatureCelsius).HasPrecision(8, 3);
        e.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 6);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.Property(x => x.EnergyScope).HasMaxLength(20);
        e.Property(x => x.PowerScope).HasMaxLength(20);
        e.Property(x => x.LatestReadingsJson).HasColumnType("jsonb");
        e.Property(x => x.StopReason).HasMaxLength(40);
        e.Property(x => x.CompletionWarning).HasMaxLength(40);
        e.Ignore(x => x.CalculatedCost);
    }
}

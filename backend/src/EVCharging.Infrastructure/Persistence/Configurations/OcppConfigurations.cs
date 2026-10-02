using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVCharging.Infrastructure.Persistence.Configurations;

internal sealed class OcppChargePointConfiguration : IEntityTypeConfiguration<OcppChargePoint>
{
    public void Configure(EntityTypeBuilder<OcppChargePoint> e)
    {
        e.ToTable("ocpp_charge_points");
        e.HasKey(x => x.Id);
        e.Property(x => x.Identity).HasMaxLength(100);
        e.Property(x => x.ProtocolVersion).HasMaxLength(12);
        e.Property(x => x.Vendor).HasMaxLength(64);
        e.Property(x => x.Model).HasMaxLength(64);
        e.Property(x => x.ChargePointSerialNumber).HasMaxLength(64);
        e.Property(x => x.ChargeBoxSerialNumber).HasMaxLength(64);
        e.Property(x => x.FirmwareVersion).HasMaxLength(64);
        e.Property(x => x.Iccid).HasMaxLength(32);
        e.Property(x => x.Imsi).HasMaxLength(32);
        e.Property(x => x.MeterType).HasMaxLength(64);
        e.Property(x => x.MeterSerialNumber).HasMaxLength(64);
        e.HasIndex(x => x.Identity).IsUnique();
        e.HasIndex(x => x.LastSeenAt);
    }
}

internal sealed class OcppConnectorConfiguration : IEntityTypeConfiguration<OcppConnector>
{
    public void Configure(EntityTypeBuilder<OcppConnector> e)
    {
        e.ToTable(
            "ocpp_connectors",
            table => table.HasCheckConstraint("ck_ocpp_connector_number", "connector_number > 0")
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.ReportedStatus).HasMaxLength(30);
        e.Property(x => x.ErrorCode).HasMaxLength(50);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ChargePointId, x.ConnectorNumber }).IsUnique();
        e.HasIndex(x => x.StationId);
        e.HasOne<ChargingConnector>()
            .WithMany()
            .HasForeignKey(x => x.BusinessConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.BusinessConnectorId).IsUnique();
    }
}

internal sealed class OcppConnectionConfiguration : IEntityTypeConfiguration<OcppConnection>
{
    public void Configure(EntityTypeBuilder<OcppConnection> e)
    {
        e.ToTable(
            "ocpp_connections",
            table =>
                table.HasCheckConstraint(
                    "ck_ocpp_connection_time",
                    "disconnected_at IS NULL OR disconnected_at >= connected_at"
                )
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Subprotocol).HasMaxLength(20);
        e.Property(x => x.CloseReason).HasMaxLength(200);
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ChargePointId, x.ConnectedAt });
    }
}

internal sealed class OcppInboundMessageConfiguration : IEntityTypeConfiguration<OcppInboundMessage>
{
    public void Configure(EntityTypeBuilder<OcppInboundMessage> e)
    {
        e.ToTable("ocpp_inbound_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.CallId).HasMaxLength(100);
        e.Property(x => x.Action).HasMaxLength(60);
        e.Property(x => x.FrameJson).HasColumnType("jsonb");
        e.Property(x => x.PayloadHash).HasMaxLength(64);
        e.Property(x => x.ResponseJson).HasColumnType("jsonb");
        e.Property(x => x.ProcessingState).HasMaxLength(24);
        e.Property(x => x.FailureCode).HasMaxLength(80);
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.ConnectionId, x.CallId }).IsUnique();
        e.HasIndex(x => new { x.ChargePointId, x.ReceivedAt });
        e.HasIndex(x => new { x.ProcessingState, x.ReceivedAt });
    }
}

internal sealed class OcppStatusEventConfiguration : IEntityTypeConfiguration<OcppStatusEvent>
{
    public void Configure(EntityTypeBuilder<OcppStatusEvent> e)
    {
        e.ToTable(
            "ocpp_status_events",
            table => table.HasCheckConstraint("ck_ocpp_status_connector", "connector_number >= 0")
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasMaxLength(30);
        e.Property(x => x.ErrorCode).HasMaxLength(50);
        e.Property(x => x.Info).HasMaxLength(80);
        e.Property(x => x.VendorId).HasMaxLength(255);
        e.Property(x => x.VendorErrorCode).HasMaxLength(50);
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppInboundMessage>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.MessageId).IsUnique();
        e.HasIndex(x => new
        {
            x.ChargePointId,
            x.ConnectorNumber,
            x.ReceivedAt,
        });
    }
}

internal sealed class OcppChargingTransactionConfiguration
    : IEntityTypeConfiguration<OcppChargingTransaction>
{
    public void Configure(EntityTypeBuilder<OcppChargingTransaction> e)
    {
        e.ToTable(
            "ocpp_charging_transactions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ocpp_transaction_meter",
                    "meter_start_wh >= 0 AND (meter_stop_wh IS NULL OR meter_stop_wh >= 0)"
                );
                table.HasCheckConstraint(
                    "ck_ocpp_transaction_time",
                    "stopped_at IS NULL OR stopped_at >= started_at"
                );
            }
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Ocpp16TransactionId).ValueGeneratedOnAdd().UseIdentityByDefaultColumn();
        e.Property(x => x.ExternalTransactionId).HasMaxLength(100);
        e.Property(x => x.State).HasMaxLength(32);
        e.Property(x => x.StopReason).HasMaxLength(40);
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingStation>()
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ChargingSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppInboundMessage>()
            .WithMany()
            .HasForeignKey(x => x.StartMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppInboundMessage>()
            .WithMany()
            .HasForeignKey(x => x.StopMessageId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.Ocpp16TransactionId).IsUnique();
        e.HasIndex(x => x.StartMessageId).IsUnique();
        e.HasIndex(x => x.StopMessageId).IsUnique();
        e.HasIndex(x => new { x.ChargePointId, x.ExternalTransactionId })
            .IsUnique()
            .HasFilter("external_transaction_id IS NOT NULL");
        e.HasIndex(x => new { x.ChargePointId, x.ConnectorNumber })
            .IsUnique()
            .HasFilter("physical_closed_at IS NULL");
        e.HasIndex(x => new { x.StationId, x.StartedAt });
        e.HasIndex(x => x.SessionId).IsUnique();
    }
}

internal sealed class OcppMeterSampleConfiguration : IEntityTypeConfiguration<OcppMeterSample>
{
    public void Configure(EntityTypeBuilder<OcppMeterSample> e)
    {
        e.ToTable(
            "ocpp_meter_samples",
            table =>
                table.HasCheckConstraint(
                    "ck_ocpp_sample_index",
                    "connector_number >= 0 AND meter_value_index >= 0 AND sample_index >= 0"
                )
        );
        e.HasKey(x => x.Id);
        e.Property(x => x.Measurand).HasMaxLength(80);
        e.Property(x => x.ValueText).HasMaxLength(512);
        e.Property(x => x.NumericValue).HasPrecision(20, 8);
        e.Property(x => x.Unit).HasMaxLength(20);
        e.Property(x => x.Phase).HasMaxLength(20);
        e.Property(x => x.Location).HasMaxLength(30);
        e.Property(x => x.Context).HasMaxLength(30);
        e.Property(x => x.Format).HasMaxLength(20);
        e.HasOne<OcppChargePoint>()
            .WithMany()
            .HasForeignKey(x => x.ChargePointId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppInboundMessage>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<OcppChargingTransaction>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new
            {
                x.MessageId,
                x.MeterValueIndex,
                x.SampleIndex,
            })
            .IsUnique();
        e.HasIndex(x => new { x.TransactionId, x.ObservedAt });
        e.HasIndex(x => new
        {
            x.ChargePointId,
            x.ConnectorNumber,
            x.ReceivedAt,
        });
    }
}

using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Persistence;

public sealed class ChargingDbContext(DbContextOptions<ChargingDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ChargingStation> Stations => Set<ChargingStation>();
    public DbSet<ChargingHistory> ChargingHistory => Set<ChargingHistory>();
    public DbSet<OcppChargePoint> OcppChargePoints => Set<OcppChargePoint>();
    public DbSet<OcppConnector> OcppConnectors => Set<OcppConnector>();
    public DbSet<OcppConnection> OcppConnections => Set<OcppConnection>();
    public DbSet<OcppInboundMessage> OcppInboundMessages => Set<OcppInboundMessage>();
    public DbSet<OcppStatusEvent> OcppStatusEvents => Set<OcppStatusEvent>();
    public DbSet<OcppChargingTransaction> OcppChargingTransactions =>
        Set<OcppChargingTransaction>();
    public DbSet<OcppMeterSample> OcppMeterSamples => Set<OcppMeterSample>();
    public DbSet<RealtimeOutboxEvent> RealtimeOutbox => Set<RealtimeOutboxEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfiguration(new AccountConfiguration());
        model.ApplyConfiguration(new LoginSessionConfiguration());
        model.ApplyConfiguration(new DeviceRegistrationConfiguration());
        model.ApplyConfiguration(new UserNotificationConfiguration());
        model.ApplyConfiguration(new PushDeliveryConfiguration());
        model.ApplyConfiguration(new AuditEntryConfiguration());
        model.ApplyConfiguration(new ChargingStationConfiguration());
        model.ApplyConfiguration(new ChargingHistoryConfiguration());
        model.ApplyConfiguration(new OcppChargePointConfiguration());
        model.ApplyConfiguration(new OcppConnectorConfiguration());
        model.ApplyConfiguration(new OcppConnectionConfiguration());
        model.ApplyConfiguration(new OcppInboundMessageConfiguration());
        model.ApplyConfiguration(new OcppStatusEventConfiguration());
        model.ApplyConfiguration(new OcppChargingTransactionConfiguration());
        model.ApplyConfiguration(new OcppMeterSampleConfiguration());
        model.ApplyConfiguration(new RealtimeOutboxEventConfiguration());
        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(
                System
                    .Text.RegularExpressions.Regex.Replace(
                        property.Name,
                        "([a-z0-9])([A-Z])",
                        "$1_$2"
                    )
                    .ToLowerInvariant()
            );
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (
            ChangeTracker
                .Entries<AuditEntry>()
                .Any(e => e.State is EntityState.Modified or EntityState.Deleted)
        )
            throw new InvalidOperationException("Audit entries are append-only.");
        return base.SaveChangesAsync(cancellationToken);
    }
}

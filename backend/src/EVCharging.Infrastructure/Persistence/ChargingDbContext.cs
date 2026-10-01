using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Persistence;

public sealed class ChargingDbContext(DbContextOptions<ChargingDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<ChargingStation> Stations => Set<ChargingStation>();
    public DbSet<ChargingHistory> ChargingHistory => Set<ChargingHistory>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.ToTable("accounts", table =>
            {
                table.HasCheckConstraint("ck_account_role", "role IN ('Driver', 'Admin', 'Superadmin')");
                table.HasCheckConstraint("ck_account_language", "language IN ('Sq', 'En')");
            });
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
        });
        model.Entity<LoginSession>(e =>
        {
            e.ToTable("login_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceRegistration>(e =>
        {
            e.ToTable("device_registrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(256);
            e.Property(x => x.Platform).HasMaxLength(10);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LoginSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserNotification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceEventId).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
            e.HasIndex(x => new { x.UserId, x.SourceEventId, x.Kind }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PushDelivery>(e =>
        {
            e.ToTable("push_deliveries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.NotificationId, x.DeviceId }).IsUnique();
            e.HasIndex(x => new { x.CompletedAt, x.NextAttemptAt });
            e.Property(x => x.TicketId).HasMaxLength(128);
            e.Property(x => x.FailureCode).HasMaxLength(80);
            e.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DeviceRegistration>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorRole).HasMaxLength(20);
            e.Property(x => x.Action).HasMaxLength(80);
            e.HasIndex(x => new { x.ActorId, x.RecordedAt });
        });
        model.Entity<ChargingStation>(e =>
        {
            e.ToTable("charging_stations", table =>
            {
                table.HasCheckConstraint("ck_station_coordinates", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180");
                table.HasCheckConstraint("ck_station_capacity", "connector_count > 0 AND power_kw > 0 AND price_per_kwh >= 0");
            });
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
            e.Property(x => x.ForecastSource).HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.City, x.IsArchived });
        });
        model.Entity<ChargingHistory>(e =>
        {
            e.ToTable("charging_history", table =>
                table.HasCheckConstraint("ck_history_measurement", "energy_kwh >= 0 AND unit_price_snapshot >= 0 AND total_cost >= 0 AND ended_at >= started_at"));
            e.HasKey(x => x.Id);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.EnergyKWh).HasPrecision(12, 3);
            e.Property(x => x.UnitPriceSnapshot).HasPrecision(10, 4);
            e.Property(x => x.TotalCost).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => new { x.DriverId, x.StartedAt });
        });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit entries are append-only.");
        return base.SaveChangesAsync(cancellationToken);
    }
}

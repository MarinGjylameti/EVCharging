using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EVCharging.Tests;

public sealed partial class PostgreSqlChargingTests
{
    [PostgreSqlFact]
    public async Task AdministrationMigrationScopesAndPhysicalProvisioningUseRealSqlAndConstraints()
    {
        await using var test = await DatabaseTest.CreateAsync();
        Guid superId,
            adminId;
        await using (var db = test.Context())
        {
            var super = new Account
            {
                Email = "super@pg.test",
                NormalizedEmail = "SUPER@PG.TEST",
                PasswordHash = "test-only",
                Role = UserRole.Superadmin,
            };
            var admin = new Account
            {
                Email = "admin@pg.test",
                NormalizedEmail = "ADMIN@PG.TEST",
                PasswordHash = "test-only",
                Role = UserRole.Admin,
            };
            db.Accounts.AddRange(super, admin);
            await db.SaveChangesAsync();
            superId = super.Id;
            adminId = admin.Id;
            Assert.False(db.Database.HasPendingModelChanges());
        }
        LocationDto location;
        StationDto station;
        DeviceConnectionDetails device;
        using (var scope = test.Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
            location = await service.SaveLocationAsync(
                superId,
                null,
                new("Site", "City", "Street", 41.32, 19.81),
                default
            );
            var stations = scope.ServiceProvider.GetRequiredService<IStationManagement>();
            station = await stations.CreateAsync(
                new(
                    "REAL-PG",
                    "Real point",
                    "City",
                    "Street",
                    41.32,
                    19.81,
                    "CCS2",
                    2,
                    50,
                    .35m,
                    "Available",
                    LocationId: location.Id
                ),
                superId,
                default
            );
            await service.SaveAdministratorAsync(
                superId,
                adminId,
                new("admin@pg.test", "Admin", "Test", null, null, true, [location.Id], [], 1),
                default
            );
            device = await service.ProvisionDeviceAsync(
                adminId,
                station.Id,
                new("CP-PG", "SERIAL-PG", null, null, "1.6J", ["CARD-PG"]),
                default
            );
        }
        using (var scope = test.Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
            var rows = await scope
                .ServiceProvider.GetRequiredService<IStationManagement>()
                .ListAsync(adminId, default);
            Assert.Equal(station.Id, Assert.Single(rows).Id);
            Assert.Equal(1, (await service.SummaryAsync(adminId, default)).Stations);
            Assert.Equal(
                0,
                (await service.SessionsAsync(adminId, new(Driver: "unknown"), default)).Total
            );
            Assert.Single(await service.PricesAsync(adminId, null, default));
            Assert.NotEmpty(await service.AuditAsync(adminId, null, default));
            var gateway = scope.ServiceProvider.GetRequiredService<IOcppGateway>();
            Assert.Equal(
                device.Device.Id,
                await gateway.AuthenticateAsync(device.Device.Identity, "", "", default)
            );
            var connection = await gateway.OpenAsync(device.Device.Id, default);
            Assert.True(
                await gateway.IsConnectionCurrentAsync(device.Device.Id, connection, default)
            );
        }
        await using (var db = test.Context())
        {
            db.AdministratorGrants.Add(
                new AdministratorGrant { AdministratorId = adminId, LocationId = location.Id }
            );
            var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(conflict.InnerException).SqlState
            );
        }
        await using (var db = test.Context())
        {
            db.AdministratorGrants.Add(
                new AdministratorGrant
                {
                    AdministratorId = adminId,
                    LocationId = location.Id,
                    StationId = station.Id,
                }
            );
            var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(invalid.InnerException).SqlState
            );
        }
    }
}

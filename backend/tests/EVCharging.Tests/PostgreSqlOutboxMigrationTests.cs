using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EVCharging.Tests;

public sealed partial class PostgreSqlChargingTests
{
    [PostgreSqlFact]
    public async Task OperationsAudienceUpgradePreservesOutboxAndAllowsApplicationStartup()
    {
        await using var test = await DatabaseTest.CreateAsync(
            _ => Task.CompletedTask,
            migrateToLatest: false
        );
        await using var db = test.Context();
        await db.GetService<IMigrator>()
            .MigrateAsync("20261004103819_AdministrationAndPhysicalChargers");
        var old = OutboxEntry("PublicStation");
        db.RealtimeOutbox.Add(old);
        await db.SaveChangesAsync();

        var operations = OutboxEntry("Operations");
        db.RealtimeOutbox.Add(operations);
        var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var cause = Assert.IsType<PostgresException>(rejected.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, cause.SqlState);
        Assert.Equal("ck_realtime_outbox_audience", cause.ConstraintName);
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();
        db.RealtimeOutbox.Add(operations);
        await db.SaveChangesAsync();
        Assert.Equal(
            "PublicStation",
            (await db.RealtimeOutbox.SingleAsync(x => x.Id == old.Id)).Audience
        );
        Assert.Equal(
            old.PayloadJson,
            (await db.RealtimeOutbox.SingleAsync(x => x.Id == old.Id)).PayloadJson
        );
        Assert.Equal(
            "Operations",
            (await db.RealtimeOutbox.SingleAsync(x => x.Id == operations.Id)).Audience
        );
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // The upgrade must retain the private-event recipient requirement.
        db.RealtimeOutbox.Add(OutboxEntry("Driver"));
        var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            Assert.IsType<PostgresException>(invalid.InnerException).SqlState
        );
        db.ChangeTracker.Clear();

        // Uses the real PostgreSQL schema for demo initialization and startup reconciliation.
        await using var factory = new ApiFactory(
            simulator: true,
            postgresConnection: db.Database.GetConnectionString()
        );
        using var client = factory.CreateClient();
        Assert.NotEmpty(await db.OcppChargePoints.ToListAsync());
        Assert.True(await db.RealtimeOutbox.AnyAsync(x => x.Audience == "Operations"));
    }

    private static RealtimeOutboxEvent OutboxEntry(string audience) =>
        new()
        {
            EventType = "StationChanged",
            EntityType = "ChargingStation",
            EntityId = Guid.NewGuid(),
            EntityVersion = 1,
            Audience = audience,
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow,
        };
}

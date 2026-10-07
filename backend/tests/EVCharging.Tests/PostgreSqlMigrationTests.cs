using System.Text.Json;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVCharging.Tests;

public sealed partial class PostgreSqlChargingTests
{
    private const string DemoOwnerAudit = "ChargingBusinessSessions.DemoOwnerUnlinked";

    [PostgreSqlFact]
    public async Task MigrationPreservesConflictingDemoSessionsWithoutChoosingAnOwner()
    {
        var driver = LegacyDriver("demo");
        var first = new LegacySession(driver.Id);
        var second = new LegacySession(driver.Id);
        var completed = new LegacySession(driver.Id) { Completed = true };
        await using var test = await DatabaseTest.CreateAsync(db =>
            SeedLegacyAsync(db, [driver], first, second, completed)
        );
        await using var db = test.Context();
        var sessions = await db.ChargingSessions.ToListAsync();
        Assert.Equal(3, sessions.Count);
        foreach (var legacy in new[] { first, second, completed })
        {
            var session = Assert.Single(sessions, x => x.Id == legacy.Id);
            Assert.Equal(legacy.Started, session.StartedAt);
            Assert.Equal(legacy.Received, session.StartReceivedAt);
            Assert.Equal(.35m, session.UnitPriceSnapshot);
            Assert.Equal(2m, session.EnergyKWh);
            Assert.Equal(.70m, session.CalculatedCost);
            Assert.Equal(legacy.Completed, session.EndedAt is not null);
        }
        var claim = await db.ChargingConnectionRequests.SingleAsync();
        Assert.Equal(completed.Id, claim.SessionId);
        Assert.Equal(driver.Id, claim.DriverId);
        Assert.Equal("Completed", claim.State);
        var history = await db.ChargingHistory.SingleAsync(x => x.StationId == completed.StationId);
        Assert.Equal(driver.Id, history.DriverId);
        Assert.Equal(.70m, history.TotalCost);
        var audits = await db.Audit.Where(x => x.Action == DemoOwnerAudit).ToListAsync();
        Assert.Equal(2, audits.Count);
        foreach (var legacy in new[] { first, second })
        {
            var audit = Assert.Single(audits, x => x.EntityId == legacy.Id);
            Assert.Equal("Migration", audit.ActorRole);
            using var json = JsonDocument.Parse(audit.ChangesJson!);
            var original = json.RootElement.GetProperty("legacyTransaction");
            Assert.Equal(driver.Id, original.GetProperty("driver_id").GetGuid());
            Assert.Equal(legacy.StationId, original.GetProperty("station_id").GetGuid());
            Assert.Equal(.35m, original.GetProperty("unit_price_snapshot").GetDecimal());
            Assert.Equal(.70m, original.GetProperty("calculated_cost").GetDecimal());
        }
        var transactions = await db.OcppChargingTransactions.ToListAsync();
        Assert.Equal(3, transactions.Count);
        Assert.All(transactions, x => Assert.Equal(x.Id, x.SessionId));

        // A retry does not duplicate the audit, and the normal claim flow can adopt a preserved session.
        await db.Database.MigrateAsync();
        Assert.Equal(2, await db.Audit.CountAsync(x => x.Action == DemoOwnerAudit));
        Assert.Null(await test.ClaimAsync(driver.Id, first.StationId));
        var adopted = await db.ChargingConnectionRequests.SingleAsync(x => x.State == "Linked");
        Assert.Equal(first.Id, adopted.SessionId);
        Assert.Equal(driver.Id, adopted.DriverId);
        Assert.Equal(
            "driver_already_connected",
            await test.ClaimAsync(driver.Id, second.StationId)
        );
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task MigrationKeepsExplicitOwnerAndOnlyDefersConflictingDemoFallback()
    {
        var fallbackDriver = LegacyDriver("fallback");
        var explicitDriver = LegacyDriver("explicit");
        var explicitSession = new LegacySession(fallbackDriver.Id)
        {
            ClaimOwner = explicitDriver.Id,
        };
        var conflicting = new LegacySession(explicitDriver.Id);
        var independent = new LegacySession(fallbackDriver.Id);
        await using var test = await DatabaseTest.CreateAsync(db =>
            SeedLegacyAsync(
                db,
                [fallbackDriver, explicitDriver],
                explicitSession,
                conflicting,
                independent
            )
        );
        await using var db = test.Context();
        var claims = await db.ChargingConnectionRequests.ToListAsync();
        Assert.Equal(2, claims.Count);
        var explicitClaim = Assert.Single(claims, x => x.SessionId == explicitSession.Id);
        Assert.Equal(explicitDriver.Id, explicitClaim.DriverId);
        Assert.Equal(explicitSession.ClaimId, explicitClaim.Id);
        Assert.Equal(explicitSession.Received.AddMinutes(1), explicitClaim.LinkedAt);
        Assert.Equal(
            fallbackDriver.Id,
            Assert.Single(claims, x => x.SessionId == independent.Id).DriverId
        );
        Assert.Equal(
            conflicting.Id,
            (await db.Audit.SingleAsync(x => x.Action == DemoOwnerAudit)).EntityId
        );
        Assert.Equal(3, await db.ChargingSessions.CountAsync());
    }

    [PostgreSqlFact]
    public async Task MigrationKeepsWaitingClaimsWhenDemoFallbackConflictsByDriverOrConnector()
    {
        foreach (var sameDriver in new[] { true, false })
        {
            var driver = LegacyDriver("demo");
            var other = LegacyDriver("waiting");
            var session = new LegacySession(driver.Id);
            var waitingStation = new LegacySession(null);
            var waiting = new ChargingConnectionRequest
            {
                DriverId = sameDriver ? driver.Id : other.Id,
                StationId = sameDriver ? waitingStation.StationId : session.StationId,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            };
            await using var test = await DatabaseTest.CreateAsync(async db =>
            {
                await SeedLegacyAsync(db, [driver, other], session, waitingStation);
                await db.Database.ExecuteSqlInterpolatedAsync(LegacyMigrationSeed.Claim(waiting));
            });
            await using var db = test.Context();
            var claim = await db.ChargingConnectionRequests.SingleAsync();
            Assert.Equal(waiting.Id, claim.Id);
            Assert.Equal("Waiting", claim.State);
            Assert.Null(claim.SessionId);
            Assert.Equal(
                session.Id,
                (await db.Audit.SingleAsync(x => x.Action == DemoOwnerAudit)).EntityId
            );
            Assert.Equal(2, await db.ChargingSessions.CountAsync());
        }
    }

    [PostgreSqlFact]
    public async Task MigrationNormalizesExpiredWaitingAndStoppedClaimsBeforeCheckingConflicts()
    {
        var driver = LegacyDriver("demo");
        var active = new LegacySession(driver.Id);
        var stopped = new LegacySession(driver.Id) { Completed = true, ClaimOwner = driver.Id };
        var expired = new ChargingConnectionRequest
        {
            DriverId = driver.Id,
            StationId = active.StationId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        };
        await using var test = await DatabaseTest.CreateAsync(async db =>
        {
            await SeedLegacyAsync(db, [driver], active, stopped);
            await db.Database.ExecuteSqlInterpolatedAsync(LegacyMigrationSeed.Claim(expired));
        });
        await using var db = test.Context();
        Assert.Equal(
            "Expired",
            (await db.ChargingConnectionRequests.SingleAsync(x => x.Id == expired.Id)).State
        );
        Assert.Equal(
            "Completed",
            (await db.ChargingConnectionRequests.SingleAsync(x => x.Id == stopped.ClaimId)).State
        );
        Assert.Equal(
            active.Id,
            (await db.ChargingConnectionRequests.SingleAsync(x => x.State == "Linked")).SessionId
        );
        Assert.Empty(await db.Audit.Where(x => x.Action == DemoOwnerAudit).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task MigrationRejectsExplicitOrNonDemoConflictsAndRollsBackWithLegacyOwnersIntact()
    {
        foreach (var explicitClaims in new[] { true, false })
        {
            var driver = LegacyDriver("conflict");
            var first = new LegacySession(driver.Id)
            {
                IsDemo = explicitClaims,
                ClaimOwner = explicitClaims ? driver.Id : null,
            };
            var second = new LegacySession(driver.Id)
            {
                IsDemo = explicitClaims,
                ClaimOwner = explicitClaims ? driver.Id : null,
            };
            // This demo association would be audited, but the later real conflict must roll it back too.
            var demo = new LegacySession(driver.Id);
            await using var test = await DatabaseTest.CreateAsync(
                db => SeedLegacyAsync(db, [driver], first, second, demo),
                migrateToLatest: false
            );
            await using var db = test.Context();
            var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.MigrateAsync()
            );
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Conflicting legacy active claims", error.MessageText);
            Assert.Contains("Reconcile the listed claims", error.Hint);
            Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Empty(await db.Audit.Where(x => x.Action == DemoOwnerAudit).ToListAsync());
            var owners = await db
                .Database.SqlQueryRaw<Guid>(
                    """SELECT driver_id AS "Value" FROM ocpp_charging_transactions"""
                )
                .ToListAsync();
            Assert.Equal(3, owners.Count);
            Assert.All(owners, owner => Assert.Equal(driver.Id, owner));
            Assert.Equal(explicitClaims ? 2 : 0, await db.ChargingConnectionRequests.CountAsync());
            Assert.True(
                await db
                    .Database.SqlQueryRaw<bool>(
                        """SELECT to_regclass('charging_sessions') IS NULL AS "Value"""
                    )
                    .SingleAsync()
            );
        }
    }

    private static Account LegacyDriver(string name) =>
        new()
        {
            Email = name + "@test.invalid",
            NormalizedEmail = name.ToUpperInvariant() + "@TEST.INVALID",
            PasswordHash = "test-only",
        };

    private sealed record LegacySession(Guid? DriverId)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid StationId { get; } = Guid.NewGuid();
        public Guid ClaimId { get; } = Guid.NewGuid();
        public Guid? ClaimOwner { get; init; }
        public bool IsDemo { get; init; } = true;
        public bool Completed { get; init; }
        public DateTimeOffset Started { get; } = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Received => Started.AddSeconds(3);
    }

    private static async Task SeedLegacyAsync(
        ChargingDbContext db,
        Account[] drivers,
        params LegacySession[] sessions
    )
    {
        await InsertLegacyAccountsAsync(db, drivers);
        var number = 0;
        foreach (var legacy in sessions)
        {
            var station = new ChargingStation
            {
                Id = legacy.StationId,
                Code = "LEGACY-" + ++number,
                Name = "Legacy migration test",
                PricePerKWh = .40m,
                ConnectorCount = 1,
                ConnectorType = "Type 2",
                PowerKW = 22,
                IsDemo = legacy.IsDemo,
            };
            var device = new OcppChargePoint { Identity = station.Code, IsEnabled = true };
            var connection = new OcppConnection
            {
                ChargePointId = device.Id,
                ConnectedAt = legacy.Received,
            };
            var message = new OcppInboundMessage
            {
                ChargePointId = device.Id,
                ConnectionId = connection.Id,
                CallId = "legacy-start",
                Action = "StartTransaction",
                FrameJson = "[]",
                PayloadHash = "test",
                ReceivedAt = legacy.Received,
            };
            await db.Database.ExecuteSqlInterpolatedAsync(LegacyMigrationSeed.Station(station));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ocpp_charge_points (id, identity, protocol_version, is_enabled)
                VALUES ({device.Id}, {device.Identity}, '1.6J', true);
                """
            );
            await db.Database.ExecuteSqlInterpolatedAsync(
                LegacyMigrationSeed.Connection(connection)
            );
            await db.Database.ExecuteSqlInterpolatedAsync(LegacyMigrationSeed.Message(message));
            DateTimeOffset? stopped = legacy.Completed ? legacy.Started.AddMinutes(20) : null;
            var state = legacy.Completed ? "Stopped" : "Active";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ocpp_connectors (id, charge_point_id, connector_number, station_id, reported_status, version)
                VALUES ({Guid.NewGuid()}, {device.Id}, 1, {station.Id}, 'Charging', 1);
                INSERT INTO ocpp_charging_transactions (id, charge_point_id, connector_number, station_id, driver_id,
                    start_message_id, started_at, start_received_at, stopped_at, stop_received_at, state,
                    meter_start_wh, energy_kwh, unit_price_snapshot, currency, calculated_cost)
                VALUES ({legacy.Id}, {device.Id}, 1, {station.Id}, {legacy.DriverId}, {message.Id},
                    {legacy.Started}, {legacy.Received}, {stopped}, {stopped}, {state}, 1000, 2, 0.35, 'EUR', 0.70);
                """
            );
            if (legacy.ClaimOwner is { } owner)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    LegacyMigrationSeed.Claim(
                        new ChargingConnectionRequest
                        {
                            Id = legacy.ClaimId,
                            DriverId = owner,
                            StationId = station.Id,
                            State = "Linked",
                            CreatedAt = legacy.Received,
                            ExpiresAt = legacy.Received.AddMinutes(30),
                            LinkedAt = legacy.Received.AddMinutes(1),
                            SessionId = legacy.Id,
                        }
                    )
                );
            if (stopped is { } ended && legacy.DriverId is { } driver)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    LegacyMigrationSeed.History(
                        new ChargingHistory
                        {
                            DriverId = driver,
                            StationId = station.Id,
                            StartedAt = legacy.Started,
                            EndedAt = ended,
                            EnergyKWh = 2,
                            UnitPriceSnapshot = .35m,
                            TotalCost = .70m,
                            Currency = "EUR",
                            IsDemo = legacy.IsDemo,
                        }
                    )
                );
        }
    }
}

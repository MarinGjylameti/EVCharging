using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EVCharging.Tests;

// Deliberately opt-in: these tests connect only to a dedicated disposable database.
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (
            Environment.GetEnvironmentVariable("EVCHARGING_TEST_POSTGRES_ENABLED") != "1"
            || string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("EVCHARGING_TEST_POSTGRES")
            )
        )
            Skip =
                "Set explicit PostgreSQL test opt-in and a disposable evcharging_test* database connection.";
    }
}

public sealed partial class PostgreSqlChargingTests
{
    [PostgreSqlFact]
    public async Task MigrationPreservesLegacySessionOwnerPhysicalTimesAndTariff()
    {
        var sessionId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var driver = new Account
        {
            Email = "legacy@test.invalid",
            NormalizedEmail = "LEGACY@TEST.INVALID",
            PasswordHash = "test-only",
        };
        var station = new ChargingStation
        {
            Code = "LEGACY",
            Name = "Legacy test",
            PricePerKWh = .40m,
            ConnectorCount = 1,
            ConnectorType = "Type 2",
            PowerKW = 22,
            IsDemo = true,
        };
        var started = DateTimeOffset.UtcNow.AddHours(-1);
        var received = started.AddSeconds(3);
        await using var test = await DatabaseTest.CreateAsync(async db =>
        {
            var device = new OcppChargePoint { Identity = "LEGACY", IsEnabled = true };
            var connection = new OcppConnection
            {
                ChargePointId = device.Id,
                ConnectedAt = received,
            };
            var message = new OcppInboundMessage
            {
                ChargePointId = device.Id,
                ConnectionId = connection.Id,
                CallId = "legacy-start",
                Action = "StartTransaction",
                FrameJson = "[]",
                PayloadHash = "test",
                ReceivedAt = received,
            };
            db.Accounts.Add(driver);
            db.Stations.Add(station);
            // Seed the old schema without columns introduced by later migrations.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ocpp_charge_points (id, identity, protocol_version, is_enabled)
                VALUES ({device.Id}, {device.Identity}, '1.6J', true);
                """);
            db.OcppConnections.Add(connection);
            db.OcppInboundMessages.Add(message);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ocpp_connectors (id, charge_point_id, connector_number, station_id, reported_status, version)
                VALUES ({connectorId}, {device.Id}, 1, {station.Id}, 'Charging', 1);
                INSERT INTO ocpp_charging_transactions (id, charge_point_id, connector_number, station_id, driver_id,
                    start_message_id, started_at, start_received_at, state, meter_start_wh, energy_kwh, unit_price_snapshot, currency, calculated_cost)
                VALUES ({sessionId}, {device.Id}, 1, {station.Id}, {driver.Id}, {message.Id}, {started}, {received}, 'Active', 1000, 2, 0.35, 'EUR', 0.70);
                """
            );
        });
        await using var db = test.Context();
        var session = await db.ChargingSessions.SingleAsync();
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(connectorId, session.ConnectorId);
        Assert.Equal(started.ToUnixTimeMilliseconds(), session.StartedAt.ToUnixTimeMilliseconds());
        Assert.Equal(
            received.ToUnixTimeMilliseconds(),
            session.StartReceivedAt.ToUnixTimeMilliseconds()
        );
        Assert.Equal(.35m, session.UnitPriceSnapshot);
        Assert.Equal(2m, session.EnergyKWh);
        Assert.Equal(.70m, session.CalculatedCost);
        var claim = await db.ChargingConnectionRequests.SingleAsync();
        Assert.Equal(driver.Id, claim.DriverId);
        Assert.Equal(sessionId, claim.SessionId);
        Assert.Equal("Linked", claim.State);
        Assert.Equal(sessionId, (await db.OcppChargingTransactions.SingleAsync()).SessionId);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task ConcurrentClaimsEnforceBothInvariantsAndReturnStableConflicts()
    {
        await using var test = await DatabaseTest.CreateAsync();
        var (first, second) = await test.DriversAsync();
        var stations = await test.StationsAsync();
        var sameDriver = await RaceAsync(
            () => test.ClaimAsync(first, stations[0]),
            () => test.ClaimAsync(first, stations[1])
        );
        Assert.Single(sameDriver, x => x is null);
        Assert.Single(sameDriver, x => x == "driver_already_connected");
        await test.CancelAllWaitingAsync();
        var sameConnector = await RaceAsync(
            () => test.ClaimAsync(first, stations[0]),
            () => test.ClaimAsync(second, stations[0])
        );
        Assert.Single(sameConnector, x => x is null);
        Assert.Single(sameConnector, x => x == "charger_in_use");
        await using var db = test.Context();
        Assert.Single(
            await db
                .ChargingConnectionRequests.Where(x => x.State == "Waiting" || x.State == "Linked")
                .ToListAsync()
        );
    }

    [PostgreSqlFact]
    public async Task PhysicalStartRacesClaimAndUnownedSessionCanBeClaimedOnlyOnce()
    {
        await using var test = await DatabaseTest.CreateAsync();
        var (first, second) = await test.DriversAsync();
        var stations = await test.StationsAsync();
        var connection = await test.OpenAsync("SIM-001");
        var started = DateTimeOffset.UtcNow.AddMinutes(-3);
        var result = await RaceAsync(
            () => test.ClaimAsync(first, stations[0]),
            async () =>
            {
                await test.StartAsync(connection, started);
                return null;
            }
        );
        Assert.All(result, Assert.Null);
        Guid sessionId;
        await using (var db = test.Context())
        {
            var session = await db.ChargingSessions.SingleAsync();
            sessionId = session.Id;
            var claim = await db.ChargingConnectionRequests.SingleAsync();
            Assert.Equal("Linked", claim.State);
            Assert.Equal(sessionId, claim.SessionId);
            Assert.Equal(
                started.ToUnixTimeMilliseconds(),
                session.StartedAt.ToUnixTimeMilliseconds()
            );
        }
        Assert.Null(await test.ClaimAsync(first, stations[0])); // Idempotent retry / another app.
        Assert.Equal("driver_already_connected", await test.ClaimAsync(first, stations[1]));
        await test.StopAsync(connection);
        var unowned = await test.OpenAsync("SIM-002");
        await test.StartAsync(unowned, started);
        var twoOwners = await RaceAsync(
            () => test.ClaimAsync(first, stations[1]),
            () => test.ClaimAsync(second, stations[1])
        );
        Assert.Single(twoOwners, x => x is null);
        Assert.Single(twoOwners, x => x == "charger_in_use");
        await using var final = test.Context();
        Assert.Equal(2, await final.ChargingSessions.CountAsync());
        Assert.Single(
            await final.ChargingConnectionRequests.Where(x => x.State == "Linked").ToListAsync()
        );
        Assert.Equal(
            "Completed",
            (
                await final.ChargingConnectionRequests.SingleAsync(x => x.SessionId == sessionId)
            ).State
        );
    }

    [PostgreSqlFact]
    public async Task DatabaseIndexesRejectDirectConflictsAndTransactionRollbackIncludesOutbox()
    {
        await using var test = await DatabaseTest.CreateAsync();
        var (first, second) = await test.DriversAsync();
        var stations = await test.StationsAsync();
        Assert.Null(await test.ClaimAsync(first, stations[0]));
        foreach (
            var (owner, point, index) in new[]
            {
                (first, stations[1], "ux_claim_active_driver"),
                (second, stations[0], "ux_claim_active_connector"),
            }
        )
        {
            await using var db = test.Context();
            db.ChargingConnectionRequests.Add(
                new ChargingConnectionRequest
                {
                    DriverId = owner,
                    StationId = point,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                }
            );
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(
                index,
                Assert.IsType<PostgresException>(error.InnerException).ConstraintName
            );
        }
        var eventId = Guid.NewGuid();
        await using (var db = test.Context())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.ChargingConnectionRequests.SingleAsync()).State = "Cancelled";
            db.RealtimeOutbox.Add(
                new RealtimeOutboxEvent
                {
                    Id = eventId,
                    EventType = "RollbackProbe",
                    EntityType = "ChargingClaim",
                    EntityId = first,
                    PayloadJson = "{}",
                }
            );
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var db = test.Context())
        {
            Assert.Equal("Waiting", (await db.ChargingConnectionRequests.SingleAsync()).State);
            Assert.False(await db.RealtimeOutbox.AnyAsync(x => x.Id == eventId));
            var claim = await db.ChargingConnectionRequests.SingleAsync();
            claim.CreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
            claim.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Null(await test.ClaimAsync(first, stations[1]));
        await using var final = test.Context();
        Assert.Single(
            await final.ChargingConnectionRequests.Where(x => x.State == "Expired").ToListAsync()
        );
    }

    private static async Task<string?[]> RaceAsync(params Func<Task<string?>>[] actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions
            .Select(action =>
                Task.Run(async () =>
                {
                    await gate.Task;
                    return await action();
                })
            )
            .ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class DatabaseTest : IAsyncDisposable
    {
        private readonly string connectionString;
        private readonly string schema;
        private ApiFactory? factory;

        private DatabaseTest(string connectionString, string schema)
        {
            this.connectionString = connectionString;
            this.schema = schema;
        }

        public ChargingDbContext Context() =>
            new(
                new DbContextOptionsBuilder<ChargingDbContext>().UseNpgsql(connectionString).Options
            );

        public static async Task<DatabaseTest> CreateAsync(
            Func<ChargingDbContext, Task>? seedLegacy = null,
            bool migrateToLatest = true
        )
        {
            var builder = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("EVCHARGING_TEST_POSTGRES")!
            );
            if (
                builder.Database is null
                || !builder.Database.StartsWith(
                    "evcharging_test",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException(
                    "Refusing PostgreSQL tests: use a dedicated disposable database named evcharging_test*."
                );
            var schema = "test_" + Guid.NewGuid().ToString("N");
            builder.SearchPath = schema; // Never fall back to public or its migration history.
            builder.Pooling = false;
            builder.Timeout = 5;
            builder.CommandTimeout = 30;
            var test = new DatabaseTest(builder.ConnectionString, schema);
            try
            {
                await using var connection = new NpgsqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand(
                    $"CREATE SCHEMA \"{schema}\"",
                    connection
                );
                await create.ExecuteNonQueryAsync();
                await using var db = test.Context();
                if (seedLegacy is not null)
                {
                    await db.GetService<IMigrator>()
                        .MigrateAsync("20261001192357_DriverChargingConnections");
                    await seedLegacy(db);
                    db.ChangeTracker.Clear();
                }
                if (!migrateToLatest)
                    return test;
                await db.Database.MigrateAsync(); // Real migration SQL, keys and checks, not EnsureCreated.
                test.factory = new ApiFactory(
                    simulator: true,
                    postgresConnection: builder.ConnectionString
                );
                using var client = test.factory.CreateClient();
                return test;
            }
            catch
            {
                await test.DisposeAsync();
                throw;
            }
        }

        public async Task<(Guid, Guid)> DriversAsync()
        {
            await using var db = Context();
            var first = new Account
            {
                Email = "first@test.invalid",
                NormalizedEmail = "FIRST@TEST.INVALID",
                PasswordHash = "test-only",
            };
            var second = new Account
            {
                Email = "second@test.invalid",
                NormalizedEmail = "SECOND@TEST.INVALID",
                PasswordHash = "test-only",
            };
            db.Accounts.AddRange(first, second);
            await db.SaveChangesAsync();
            return (first.Id, second.Id);
        }

        public async Task<Guid[]> StationsAsync()
        {
            await using var db = Context();
            return await db.Stations.OrderBy(x => x.Code).Select(x => x.Id).ToArrayAsync();
        }

        public async Task<string?> ClaimAsync(Guid driver, Guid station)
        {
            using var scope = factory!.Services.CreateScope();
            try
            {
                await scope
                    .ServiceProvider.GetRequiredService<IDriverCharging>()
                    .ConnectAsync(driver, station, default);
                return null;
            }
            catch (RequestFailure error)
            {
                return error.Code;
            }
        }

        public async Task CancelAllWaitingAsync()
        {
            await using var db = Context();
            foreach (
                var claim in await db
                    .ChargingConnectionRequests.Where(x => x.State == "Waiting")
                    .ToListAsync()
            )
                claim.State = "Cancelled";
            await db.SaveChangesAsync();
        }

        public async Task<(Guid Device, Guid Connection)> OpenAsync(string identity)
        {
            using var scope = factory!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            var device = await db.OcppChargePoints.SingleAsync(x => x.Identity == identity);
            return (
                device.Id,
                await scope
                    .ServiceProvider.GetRequiredService<IOcppGateway>()
                    .OpenAsync(device.Id, default)
            );
        }

        public Task StartAsync((Guid Device, Guid Connection) connection, DateTimeOffset started) =>
            CallAsync(
                connection,
                "StartTransaction",
                new
                {
                    connectorId = 1,
                    idTag = "TAG-001",
                    meterStart = 1000,
                    timestamp = started,
                }
            );

        public async Task StopAsync((Guid Device, Guid Connection) connection)
        {
            await using var db = Context();
            var transaction = await db.OcppChargingTransactions.SingleAsync(x =>
                x.ChargePointId == connection.Device && x.StoppedAt == null
            );
            await CallAsync(
                connection,
                "StopTransaction",
                new
                {
                    transactionId = transaction.Ocpp16TransactionId,
                    meterStop = 2000,
                    timestamp = DateTimeOffset.UtcNow,
                }
            );
        }

        private async Task CallAsync(
            (Guid Device, Guid Connection) connection,
            string action,
            object payload
        )
        {
            using var scope = factory!.Services.CreateScope();
            var response = await scope
                .ServiceProvider.GetRequiredService<IOcppGateway>()
                .ProcessAsync(
                    connection.Device,
                    connection.Connection,
                    JsonSerializer.Serialize(
                        new object[] { 2, Guid.NewGuid().ToString(), action, payload }
                    ),
                    default
                );
            using var json = JsonDocument.Parse(response);
            Assert.Equal(3, json.RootElement[0].GetInt32());
        }

        public async ValueTask DisposeAsync()
        {
            if (factory is not null)
                await factory.DisposeAsync();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            // schema is generated here from a GUID; the dedicated database name was validated before creation.
            await using var drop = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",
                connection
            );
            await drop.ExecuteNonQueryAsync();
        }
    }
}

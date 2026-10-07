using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class PersistentApplicationLogTests
{
    private static readonly IOptions<DiagnosticOptions> Settings = Options.Create(
        new DiagnosticOptions()
    );

    [Fact]
    public void PostgreSqlMigrationMatchesModelAndGeneratesAnAdditiveScriptWithoutConnecting()
    {
        using var db = new ChargingDbContext(
            new DbContextOptionsBuilder<ChargingDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=diagnostic_script_only")
                .Options
        );
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>()
            .GenerateScript(
                "20261005103516_PhysicalChargerDriverAssignments",
                "20261007092825_PersistentApplicationLogs"
            );
        Assert.Contains("CREATE TABLE application_logs", script);
        Assert.Contains("jsonb", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("ALTER TABLE ocpp", script);
    }

    [Fact]
    public async Task HistorySurvivesANewProcessAndRetryDoesNotDuplicateEvents()
    {
        var dbOptions = DatabaseOptions();
        var diagnostics = new ApplicationDiagnostics(TimeProvider.System);
        var connection = Guid.NewGuid();
        using (diagnostics.BeginScope(new(TraceId: "trace-1", ConnectionId: connection)))
            diagnostics.Record(
                "OCPP",
                "Warning",
                "Frame rejected",
                "CP-001",
                new(MessageId: "call-7", ErrorCode: "ProtocolError")
            );
        var batch = diagnostics.PendingBatch();
        await using (var db = new ChargingDbContext(dbOptions))
        {
            var store = Store(db, diagnostics);
            await store.PersistAsync(batch, default);
            await store.PersistAsync(batch, default); // Lost commit acknowledgement.
            Assert.Equal(1, await db.ApplicationLogs.CountAsync());
        }
        var restarted = new ApplicationDiagnostics(TimeProvider.System);
        Assert.Empty(restarted.Read().Entries);
        await using var readDb = new ChargingDbContext(dbOptions);
        var page = await Store(readDb, restarted)
            .ReadAsync(new(Identity: "cp-", TraceId: "trace-1", ConnectionId: connection), default);
        var saved = Assert.Single(page.Entries);
        Assert.Equal(batch[0].EventId, saved.EventId);
        Assert.Equal(batch[0].InstanceId, saved.InstanceId);
        Assert.Equal("call-7", saved.Context!.MessageId);
        Assert.Equal("ProtocolError", saved.Context.ErrorCode);
    }

    [Fact]
    public async Task CursorFiltersAndRetentionKeepTheInvestigationStable()
    {
        var diagnostics = new ApplicationDiagnostics(TimeProvider.System);
        diagnostics.Record("OCPP", "Warning", "old warning", "CP-001");
        diagnostics.Record("OCPP", "Warning", "new warning", "CP-001");
        diagnostics.Record("HTTP", "Error", "other source");
        await using var db = new ChargingDbContext(DatabaseOptions());
        var store = Store(db, diagnostics);
        await store.PersistAsync(diagnostics.PendingBatch(), default);
        var filter = new ApplicationLogFilter(
            Source: "OCPP",
            Level: "Warning",
            Identity: "cp-",
            Limit: 1
        );
        var first = await store.ReadAsync(filter, default);
        Assert.Equal("new warning", Assert.Single(first.Entries).Message);
        Assert.NotNull(first.NextBeforeId);
        var second = await store.ReadAsync(filter with { BeforeId = first.NextBeforeId }, default);
        Assert.Equal("old warning", Assert.Single(second.Entries).Message);
        Assert.Null(second.NextBeforeId);
        Assert.Empty(
            (
                await store.ReadAsync(
                    filter with
                    {
                        From = DateTimeOffset.UtcNow.AddDays(1),
                    },
                    default
                )
            ).Entries
        );
        var old = await db.ApplicationLogs.SingleAsync(x => x.Message == "old warning");
        old.RecordedAt = DateTimeOffset.UtcNow.AddDays(-91);
        await db.SaveChangesAsync();
        await store.PruneAsync(default);
        Assert.Equal(2, await db.ApplicationLogs.CountAsync());
    }

    [Fact]
    public async Task WriterRetriesAfterDatabaseFailureWithoutLosingTheBatch()
    {
        var failure = new FailOnce();
        var dbOptions = new DbContextOptionsBuilder<ChargingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(failure)
            .Options;
        var services = new ServiceCollection();
        var diagnostics = new ApplicationDiagnostics(TimeProvider.System);
        services.AddSingleton(diagnostics);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Settings);
        services.AddScoped(_ => new ChargingDbContext(dbOptions));
        services.AddScoped<ApplicationLogStore>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new ApplicationLogWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            diagnostics,
            Settings,
            NullLogger<ApplicationLogWorker>.Instance
        );
        diagnostics.Record("System", "Error", "Important event");
        await worker.FlushAsync(default);
        Assert.Equal(2, diagnostics.StorageStatus().Pending); // Original event plus persistence failure.
        Assert.Equal("DbUpdateException", diagnostics.StorageStatus().FailureCode);
        await worker.FlushAsync(default);
        Assert.Equal(0, diagnostics.StorageStatus().Pending);
        Assert.Null(diagnostics.StorageStatus().FailureCode);
        await using var db = new ChargingDbContext(dbOptions);
        Assert.Equal(3, await db.ApplicationLogs.CountAsync()); // Original, failure, recovery.
    }

    [Fact]
    public async Task ScopesAreIsolatedAndQueueOverflowIsVisible()
    {
        var diagnostics = new ApplicationDiagnostics(
            TimeProvider.System,
            Options.Create(new DiagnosticOptions { QueueCapacity = 200 })
        );
        await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        using var scope = diagnostics.BeginScope(new(TraceId: "trace-" + i));
                        await Task.Yield();
                        diagnostics.Record("HTTP", "Information", "request-" + i);
                    })
                )
        );
        foreach (var entry in diagnostics.Read().Entries)
            Assert.Equal(entry.Message.Replace("request-", "trace-"), entry.Context!.TraceId);
        diagnostics.Record("System", "Information", "outside scope");
        Assert.Null(diagnostics.Read().Entries[0].Context!.TraceId);
        for (var n = 0; n < 200; n++)
            diagnostics.Record("HTTP", "Information", "overflow");
        Assert.Equal(200, diagnostics.StorageStatus().Pending);
        Assert.Equal(21, diagnostics.StorageStatus().Dropped);
        var batch = diagnostics.PendingBatch();
        diagnostics.Acknowledge(batch);
        Assert.Equal(0, diagnostics.StorageStatus().Pending);
    }

    private static DbContextOptions<ChargingDbContext> DatabaseOptions() =>
        new DbContextOptionsBuilder<ChargingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static ApplicationLogStore Store(
        ChargingDbContext db,
        ApplicationDiagnostics diagnostics
    ) => new(db, diagnostics, Settings, TimeProvider.System);

    private sealed class FailOnce : SaveChangesInterceptor
    {
        private bool fail = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (fail)
            {
                fail = false;
                throw new DbUpdateException("private connection details must never appear");
            }
            return ValueTask.FromResult(result);
        }
    }
}

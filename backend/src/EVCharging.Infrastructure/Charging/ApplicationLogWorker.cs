using EVCharging.Application.Charging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class ApplicationLogWorker(
    IServiceScopeFactory scopes,
    ApplicationDiagnostics diagnostics,
    IOptions<DiagnosticOptions> options,
    ILogger<ApplicationLogWorker> logger
) : BackgroundService
{
    private readonly SemaphoreSlim writer = new(1, 1);
    private int ticks;

    public override Task StartAsync(CancellationToken ct)
    {
        diagnostics.Record(
            "System",
            "Information",
            "API process starting.",
            context: new(EventName: "process_starting")
        );
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.FlushSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await FlushAsync(ct);
                if (++ticks % Math.Max(1, 60 / options.Value.FlushSeconds) == 0)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        await using var scope = scopes.CreateAsyncScope();
                        await scope
                            .ServiceProvider.GetRequiredService<ApplicationLogStore>()
                            .PruneAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception error)
                    {
                        // No raw exception messages, SQL, connection strings, or recursion into this queue.
                        logger.LogWarning(
                            "Diagnostic retention retry required: {ErrorType}.",
                            error.GetType().Name
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try
        {
            // Drain more than one batch during bursts, but yield periodically to shutdown/retention.
            for (var n = 0; n < 50; n++)
            {
                var batch = diagnostics.PendingBatch();
                if (batch.Count == 0)
                    return;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await using var scope = scopes.CreateAsyncScope();
                    await scope
                        .ServiceProvider.GetRequiredService<ApplicationLogStore>()
                        .PersistAsync(batch, timeout.Token);
                    var recovered = diagnostics.StorageStatus().FailureCode is not null;
                    diagnostics.Acknowledge(batch);
                    if (recovered)
                        diagnostics.Record(
                            "System",
                            "Information",
                            "Diagnostic persistence recovered; queued events are being saved.",
                            context: new(EventName: "diagnostic_storage_recovered")
                        );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    var code = error.GetType().Name;
                    var first = diagnostics.StorageStatus().FailureCode is null;
                    diagnostics.PersistenceFailed(code);
                    if (first)
                    {
                        diagnostics.Record(
                            "System",
                            "Error",
                            "Diagnostic persistence unavailable; events retained for retry and emitted to stdout.",
                            context: new(EventName: "diagnostic_storage_failed", ErrorType: code)
                        );
                        logger.LogError(
                            "Diagnostic persistence unavailable ({ErrorType}); batch retained for retry. Consult DiagnosticEvent stdout records.",
                            code
                        );
                    }
                    return;
                }
            }
        }
        finally
        {
            writer.Release();
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        diagnostics.Record(
            "System",
            "Information",
            "API process stopping; flushing diagnostics.",
            context: new(EventName: "process_stopping")
        );
        try
        {
            await FlushAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        if (diagnostics.StorageStatus().Pending > 0)
            logger.LogError(
                "Process stopped with {Pending} diagnostic events not persisted; consult stdout.",
                diagnostics.StorageStatus().Pending
            );
    }
}

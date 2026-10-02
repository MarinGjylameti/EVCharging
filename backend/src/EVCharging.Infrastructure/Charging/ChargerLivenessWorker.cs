using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class ChargerLivenessWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    StationEventSignal signal,
    ILogger<ChargerLivenessWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await InvalidateStaleDevicesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Charger liveness will be retried.");
            }
        }
    }

    private async Task InvalidateStaleDevicesAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var cutoff = clock.GetUtcNow().AddSeconds(-options.Value.OfflineSeconds);
        var ids = await db
            .OcppChargePoints.AsNoTracking()
            .Where(x =>
                x.LastSeenAt < cutoff
                && db.OcppConnectors.Any(c =>
                    c.ChargePointId == x.Id
                    && db.ChargingConnectors.Any(b => b.Id == c.BusinessConnectorId && b.IsOnline)
                )
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            // Lock the same row as message processing, then recheck the heartbeat.
            await using var deviceScope = scopes.CreateAsyncScope();
            var deviceDb = deviceScope.ServiceProvider.GetRequiredService<ChargingDbContext>();
            await using var transaction = await OcppDeviceUpdate.BeginAsync(deviceDb, id, ct);
            var device = await deviceDb.OcppChargePoints.SingleAsync(x => x.Id == id, ct);
            if (device.LastSeenAt < cutoff)
            {
                await deviceScope
                    .ServiceProvider.GetRequiredService<OcppStationProjection>()
                    .InvalidateDeviceAsync(id, ct);
                await deviceDb.SaveChangesAsync(ct);
            }
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            signal.Notify();
        }
    }
}

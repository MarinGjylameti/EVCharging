using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationEventWorker(
    IServiceScopeFactory scopes,
    StationEventBus bus,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    ILogger<StationEventWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SimulatorEnabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
                await InvalidateStaleDevicesAsync(stoppingToken);
                var events = await db
                    .RealtimeOutbox.Where(x =>
                        x.PublishedAt == null
                        && x.AvailableAt <= clock.GetUtcNow()
                        && (x.Audience == "PublicStation" || x.Audience == "Driver")
                    )
                    .OrderBy(x => x.CreatedAt)
                    .Take(100)
                    .ToListAsync(stoppingToken);
                foreach (var entry in events)
                {
                    var change =
                        JsonSerializer.Deserialize<StationChanged>(entry.PayloadJson)
                        ?? throw new JsonException("Invalid station event.");
                    bus.Publish(change with { RecipientAccountId = entry.RecipientAccountId });
                    entry.PublishedAt = clock.GetUtcNow();
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Station events will be retried.");
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
                && db.OcppConnectors.Any(c => c.ChargePointId == x.Id && c.ReportedStatus != null)
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
        }
    }
}

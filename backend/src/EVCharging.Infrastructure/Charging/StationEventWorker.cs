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
    StationEventSignal signal,
    TimeProvider clock,
    ILogger<StationEventWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
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
                    bus.Publish(
                        change with
                        {
                            RecipientAccountId = entry.RecipientAccountId,
                            EventId = entry.Id,
                            EventType = entry.EventType,
                            EntityType = entry.EntityType,
                            EntityId = entry.EntityId,
                            EntityVersion = entry.EntityVersion,
                            OccurredAt = entry.CreatedAt,
                        }
                    );
                    entry.PublishedAt = clock.GetUtcNow();
                }
                await db.SaveChangesAsync(stoppingToken);
                if (events.Count == 100)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Station events will be retried.");
            }
            await signal.WaitAsync(stoppingToken);
        }
    }
}

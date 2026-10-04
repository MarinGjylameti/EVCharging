using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                    StationChanged change;
                    try
                    {
                        change =
                            JsonSerializer.Deserialize<StationChanged>(entry.PayloadJson)
                            ?? throw new JsonException("Invalid station event.");
                        if (
                            change.StationId == Guid.Empty
                            || entry.Audience == "Driver" && entry.RecipientAccountId is null
                            || entry.Audience == "PublicStation"
                                && (
                                    entry.RecipientAccountId is not null
                                    || change.Session is not null
                                    || change.Command is not null
                                )
                        )
                            throw new JsonException("Invalid event audience or station identity.");
                    }
                    catch (JsonException)
                    {
                        // Retain the failed row for diagnosis, while later committed
                        // events continue. A malformed payload cannot repair itself.
                        entry.Attempts++;
                        entry.LastFailureCode = "invalid_event";
                        entry.AvailableAt = clock.GetUtcNow().AddMinutes(5);
                        logger.LogError(
                            "Realtime event {EventId} has an invalid payload or audience; retry deferred.",
                            entry.Id
                        );
                        continue;
                    }
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

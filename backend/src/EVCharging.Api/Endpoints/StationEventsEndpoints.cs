using System.Text.Json;
using EVCharging.Api.Security;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Api.Endpoints;

public static class StationEventsEndpoints
{
    public static void MapStationEventsEndpoints(this WebApplication app) =>
        app.MapGet("/api/v1/driver/stations/events", StreamAsync).RequireAuthorization("Driver");

    private static async Task StreamAsync(
        HttpContext context,
        IStationEventStream stream,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IOptions<RuntimeFeatures> features
    )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted
        );
        lifetime.CancelAfter(TimeSpan.FromSeconds(55));
        var ct = lifetime.Token;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await using var events = stream.Subscribe(context.User.UserId(), ct).GetAsyncEnumerator(ct);
        var next = events.MoveNextAsync().AsTask();
        try
        {
            await context.Response.WriteAsync("retry: 1500\nevent: ready\ndata: {}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(10), ct);
                var completed = await Task.WhenAny(next, heartbeat);
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
                var sessionId = context.User.SessionId();
                if (
                    !await db
                        .Sessions.AsNoTracking()
                        .AnyAsync(
                            x =>
                                x.Id == sessionId
                                && x.RevokedAt == null
                                && x.ExpiresAt > clock.GetUtcNow()
                                && x.User.IsActive
                                && x.User.ArchivedAt == null
                                && x.User.Role == EVCharging.Domain.Identity.UserRole.Driver,
                            ct
                        )
                )
                    break;
                if (completed == next)
                {
                    if (!await next)
                        break;
                    var current = events.Current;
                    next = events.MoveNextAsync().AsTask();
                    // Apply the current policy even to persisted events produced
                    // before a restart with test mode disabled.
                    if (
                        !features.Value.TestMode
                        && (
                            current.EventType == "DeviceCommandChanged"
                            || current.Station?.IsDemo == true
                            || current.Session?.IsDemo == true
                            || await db.Stations.AnyAsync(
                                s => s.Id == current.StationId && s.IsDemo,
                                ct
                            )
                        )
                    )
                        continue;
                    // The bus filters private deltas by owner. Transport omits
                    // recipient identities and raw protocol fields entirely.
                    var data = JsonSerializer.Serialize(
                        new
                        {
                            stationId = current.StationId,
                            version = current.Version,
                            eventId = current.EventId,
                            eventType = current.EventType,
                            entityType = current.EntityType,
                            entityId = current.EntityId,
                            entityVersion = current.EntityVersion,
                            occurredAt = current.OccurredAt,
                            station = current.Station,
                            session = features.Value.TestMode || current.Session is null
                                ? current.Session
                                : current.Session with
                                {
                                    Readings = null,
                                },
                            connector = current.Connector,
                            command = features.Value.TestMode ? current.Command : null,
                        },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    );
                    await context.Response.WriteAsync($"event: changed\ndata: {data}\n\n", ct);
                }
                else
                    await context.Response.WriteAsync("event: heartbeat\ndata: {}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lifetime.Cancel();
            try
            {
                await next;
            }
            catch (OperationCanceledException) { }
        }
    }
}

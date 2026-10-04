using System.Text.Json;
using EVCharging.Api.Security;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api.Endpoints;

public static class OperationsEventsEndpoints
{
    public static async Task StreamAsync(
        HttpContext context,
        StationEventBus bus,
        IServiceScopeFactory scopes,
        TimeProvider clock
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
        await using var events = bus.SubscribeOperations(context.User.UserId(), ct)
            .GetAsyncEnumerator(ct);
        var next = events.MoveNextAsync().AsTask();
        try
        {
            await context.Response.WriteAsync("retry: 1500\nevent: ready\ndata: {}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(10), ct));
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
                var sessionId = context.User.SessionId();
                var session = await db
                    .Sessions.AsNoTracking()
                    .Include(x => x.User)
                    .SingleOrDefaultAsync(
                        x =>
                            x.Id == sessionId
                            && x.RevokedAt == null
                            && x.ExpiresAt > clock.GetUtcNow()
                            && x.User.IsActive
                            && x.User.ArchivedAt == null,
                        ct
                    );
                if (session is null || session.User.Role == UserRole.Driver)
                    break;
                if (completed == next)
                {
                    if (!await next)
                        break;
                    var change = events.Current;
                    // Recheck assignments for each event; never trust the grants
                    // held when the stream was opened. No private delta is emitted.
                    var access = scope.ServiceProvider.GetRequiredService<OperationsAccess>();
                    var allowed =
                        change.StationId != Guid.Empty
                            ? await access
                                .Stations(session.User)
                                .AnyAsync(x => x.Id == change.StationId, ct)
                            : session.User.Role == UserRole.Superadmin
                                || change.EntityType == "Administrator"
                                    && change.EntityId == session.User.Id
                                || change.EntityType == "Location"
                                    && await access
                                        .Locations(session.User)
                                        .AnyAsync(x => x.Id == change.EntityId, ct);
                    if (allowed)
                        await context.Response.WriteAsync(
                            $"event: changed\ndata: {JsonSerializer.Serialize(new { stationId = change.StationId, eventId = change.EventId, eventType = change.EventType })}\n\n",
                            ct
                        );
                    next = events.MoveNextAsync().AsTask();
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

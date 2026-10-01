using EVCharging.Application.Notifications;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Notifications;

public sealed class PushDispatcher(ChargingDbContext db, IPushTransport transport, IOptions<PushOptions> options, TimeProvider clock)
{
    public async Task<int> DispatchAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var now = clock.GetUtcNow();
        var pending = await db.PushDeliveries.Include(x => x.Notification)
            .Where(x => x.CompletedAt == null && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt).Take(25).ToListAsync(ct);
        foreach (var delivery in pending)
        {
            var device = await db.Devices.SingleOrDefaultAsync(x => x.Id == delivery.DeviceId, ct);
            var user = await db.Accounts.SingleOrDefaultAsync(x => x.Id == delivery.Notification.UserId, ct);
            var sessionActive = device is not null && await db.Sessions.AnyAsync(x =>
                x.Id == device.SessionId && x.UserId == delivery.Notification.UserId && x.RevokedAt == null && x.ExpiresAt > now, ct);
            if (device is null || !device.IsActive || device.UserId != delivery.Notification.UserId ||
                user is null || !user.IsActive || user.ArchivedAt is not null || !user.PushEnabled || !sessionActive)
            {
                delivery.CompletedAt = now;
                delivery.FailureCode = "recipient_unavailable";
                continue;
            }
            try
            {
                if (delivery.TicketId is { } ticket)
                {
                    delivery.Attempts++;
                    var receipt = await transport.CheckReceiptAsync(ticket, ct);
                    if (receipt.Delivered) { delivery.CompletedAt = now; delivery.FailureCode = null; }
                    else if (receipt.Pending && delivery.Attempts < 15) delivery.NextAttemptAt = now.AddMinutes(2);
                    else
                    {
                        delivery.FailureCode = receipt.ErrorCode ?? "receipt_timeout";
                        delivery.CompletedAt = now;
                        if (receipt.ErrorCode == "DeviceNotRegistered") device.IsActive = false;
                    }
                }
                else
                {
                    delivery.Attempts++;
                    // Resolve the current language at send time, including queued notifications.
                    var text = NotificationTemplates.Get(delivery.Notification.Kind, user.Language);
                    var result = await transport.SendAsync(new(device.Token, text, delivery.NotificationId), ct);
                    delivery.FailureCode = result.ErrorCode;
                    if (result.Accepted)
                    {
                        delivery.AcceptedAt = now;
                        delivery.TicketId = result.TicketId;
                        delivery.Attempts = 0;
                        delivery.NextAttemptAt = now.AddMinutes(1);
                    }
                    else if (result.Retryable && delivery.Attempts < options.Value.MaxAttempts)
                        delivery.NextAttemptAt = now.AddSeconds(Math.Pow(2, delivery.Attempts) * 10);
                    else delivery.CompletedAt = now;
                    if (result.ErrorCode == "DeviceNotRegistered") device.IsActive = false;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                delivery.FailureCode = "push_transport_unavailable";
                delivery.NextAttemptAt = now.AddMinutes(1);
                if (delivery.Attempts >= (delivery.TicketId is null ? options.Value.MaxAttempts : 15)) delivery.CompletedAt = now;
            }
            // Persist each result. Provider delivery remains at-least-once if the process dies after sending.
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}

public sealed class PushWorker(IServiceScopeFactory scopes, IOptions<PushOptions> options, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PushDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Push dispatch failed; queued deliveries will be retried."); }
        }
    }
}

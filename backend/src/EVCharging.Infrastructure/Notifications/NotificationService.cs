using System.Text.RegularExpressions;
using EVCharging.Application.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Notifications;

public sealed partial class NotificationService(ChargingDbContext db, TimeProvider clock)
{
    [GeneratedRegex(@"^(ExponentPushToken|ExpoPushToken)\[[A-Za-z0-9_-]+\]$")]
    private static partial Regex TokenPattern();

    public async Task RegisterDeviceAsync(Guid userId, Guid sessionId, DeviceRequest request, CancellationToken ct)
    {
        if (request.Token is null || request.Token.Length > 256 || !TokenPattern().IsMatch(request.Token))
            throw new RequestFailure(400, "invalid_push_token", "A valid Expo push token is required.");
        if (request.Platform is not ("ios" or "android"))
            throw new RequestFailure(400, "invalid_platform", "Choose ios or android.");
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), ct);
        if (session is null) throw new RequestFailure(401, "session_expired", "Please sign in again.");
        var device = await db.Devices.SingleOrDefaultAsync(x => x.Token == request.Token, ct);
        if (device is null)
        {
            device = new DeviceRegistration { Token = request.Token };
            db.Devices.Add(device);
        }
        // A device can change accounts. Outstanding deliveries retain recipient ownership checks.
        device.UserId = userId;
        device.SessionId = sessionId;
        device.Platform = request.Platform;
        device.IsActive = true;
        device.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        foreach (var device in await db.Devices.Where(x => x.UserId == userId && x.SessionId == sessionId).ToListAsync(ct))
            device.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    // Caller must resolve and authorize recipients from the domain event, never from a client userId.
    // SaveChanges is deliberately owned by that caller so the event and outbox commit together.
    public async Task EnqueueAsync(Guid recipientId, string sourceEventId, NotificationKind kind, CancellationToken ct)
    {
        if (sourceEventId.Length is < 1 or > 200 || !Enum.IsDefined(kind))
            throw new ArgumentException("Invalid notification event.");
        if (db.Notifications.Local.Any(x => x.UserId == recipientId && x.SourceEventId == sourceEventId && x.Kind == kind) ||
            await db.Notifications.AnyAsync(x => x.UserId == recipientId && x.SourceEventId == sourceEventId && x.Kind == kind, ct))
            return;
        var user = await db.Accounts.SingleAsync(x => x.Id == recipientId, ct);
        if (!user.IsActive || user.ArchivedAt is not null) return;
        var notification = new UserNotification { UserId = recipientId, SourceEventId = sourceEventId, Kind = kind, CreatedAt = clock.GetUtcNow() };
        db.Notifications.Add(notification);
        if (!user.PushEnabled) return;
        var devices = await db.Devices.Where(x => x.UserId == recipientId && x.IsActive).ToListAsync(ct);
        foreach (var device in devices)
            db.PushDeliveries.Add(new PushDelivery { NotificationId = notification.Id, Notification = notification, DeviceId = device.Id, NextAttemptAt = clock.GetUtcNow() });
    }
}

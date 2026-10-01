using EVCharging.Domain.Notifications;

namespace EVCharging.Application.Notifications;

public sealed record PushMessage(string Token, NotificationText Text, Guid NotificationId);
public sealed record PushResult(bool Accepted, bool Retryable, string? TicketId, string? ErrorCode);
public sealed record PushReceipt(bool Pending, bool Delivered, bool Retryable, string? ErrorCode);
public interface IPushTransport
{
    Task<PushResult> SendAsync(PushMessage message, CancellationToken cancellationToken);
    Task<PushReceipt> CheckReceiptAsync(string ticketId, CancellationToken cancellationToken);
}

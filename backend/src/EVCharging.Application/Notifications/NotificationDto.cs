using EVCharging.Domain.Notifications;

namespace EVCharging.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Kind,
    DateTimeOffset CreatedAt,
    NotificationText Text
);

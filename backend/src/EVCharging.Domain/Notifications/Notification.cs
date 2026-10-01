using EVCharging.Domain.Identity;

namespace EVCharging.Domain.Notifications;

public enum NotificationKind
{
    PointOutOfService,
    ChargerFaulted,
    ChargingStarted,
    ChargingCompleted,
    UnexpectedDisconnect,
    ManualStatusChanged,
    PriceChanged,
}

public sealed class DeviceRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string Token { get; set; } = "";
    public string Platform { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SourceEventId { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PushDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public UserNotification Notification { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? TicketId { get; set; }
    public string? FailureCode { get; set; }
}

public sealed record NotificationText(string Title, string Body);

public static class NotificationTemplates
{
    // Keep lock-screen payloads generic. Private details are fetched after authentication.
    public static NotificationText Get(NotificationKind kind, AppLanguage language) =>
        (kind, language) switch
        {
            (NotificationKind.PointOutOfService, AppLanguage.Sq) => new(
                "Pika jashtë funksionit",
                "Një pikë karikimi ka dalë jashtë funksionit."
            ),
            (NotificationKind.ChargerFaulted, AppLanguage.Sq) => new(
                "Defekt në karikues",
                "U raportua një defekt. Hap aplikacionin për detajet."
            ),
            (NotificationKind.ChargingStarted, AppLanguage.Sq) => new(
                "Karikimi filloi",
                "Karikimi ka filluar. Mund ta monitorosh në aplikacion."
            ),
            (NotificationKind.ChargingCompleted, AppLanguage.Sq) => new(
                "Karikimi përfundoi",
                "Përmbledhja e karikimit është gati në aplikacion."
            ),
            (NotificationKind.UnexpectedDisconnect, AppLanguage.Sq) => new(
                "Karikimi u ndërpre",
                "U raportua një shkëputje e papritur."
            ),
            (NotificationKind.ManualStatusChanged, AppLanguage.Sq) => new(
                "Gjendja u ndryshua",
                "Një administrator ndryshoi gjendjen e pikës."
            ),
            (NotificationKind.PriceChanged, AppLanguage.Sq) => new(
                "Çmimi u ndryshua",
                "Çmimi i një pike karikimi u përditësua."
            ),
            (NotificationKind.PointOutOfService, AppLanguage.En) => new(
                "Charge point out of service",
                "A charge point is now out of service."
            ),
            (NotificationKind.ChargerFaulted, AppLanguage.En) => new(
                "Charger fault",
                "A fault was reported. Open the app for details."
            ),
            (NotificationKind.ChargingStarted, AppLanguage.En) => new(
                "Charging started",
                "Charging has started. Follow the session in the app."
            ),
            (NotificationKind.ChargingCompleted, AppLanguage.En) => new(
                "Charging completed",
                "Your charging summary is ready in the app."
            ),
            (NotificationKind.UnexpectedDisconnect, AppLanguage.En) => new(
                "Charging interrupted",
                "An unexpected disconnection was reported."
            ),
            (NotificationKind.ManualStatusChanged, AppLanguage.En) => new(
                "Status changed",
                "An administrator changed the charge point status."
            ),
            (NotificationKind.PriceChanged, AppLanguage.En) => new(
                "Price updated",
                "A charge point price was updated."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}

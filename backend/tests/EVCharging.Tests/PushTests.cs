using EVCharging.Application.Notifications;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Tests;

public sealed class PushTests : IDisposable
{
    private readonly ChargingDbContext db = new(new DbContextOptionsBuilder<ChargingDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FixedClock clock = new();
    private readonly FakeTransport transport = new();
    private PushDispatcher Dispatcher() => new(db, transport, Options.Create(new PushOptions { Enabled = true }), clock);

    private async Task<(Account User, LoginSession Session, DeviceRegistration Device, PushDelivery Delivery)> Queue()
    {
        var user = new Account { PushEnabled = true, Language = AppLanguage.Sq };
        var session = new LoginSession { UserId = user.Id, User = user, ExpiresAt = clock.GetUtcNow().AddDays(1) };
        var device = new DeviceRegistration { UserId = user.Id, SessionId = session.Id, Token = "ExpoPushToken[test-device]", Platform = "android" };
        db.AddRange(user, session, device);
        await db.SaveChangesAsync();
        await new NotificationService(db, clock).EnqueueAsync(user.Id, "event-1", NotificationKind.ChargingCompleted, default);
        await db.SaveChangesAsync();
        return (user, session, device, await db.PushDeliveries.SingleAsync());
    }

    [Fact]
    public async Task DispatchUsesCurrentLanguageAndTicketIsNotDeliveryConfirmation()
    {
        var state = await Queue();
        state.User.Language = AppLanguage.En;
        await db.SaveChangesAsync();
        await Dispatcher().DispatchAsync(default);
        Assert.Equal("Charging completed", Assert.Single(transport.Messages).Text.Title);
        Assert.NotNull(state.Delivery.AcceptedAt);
        Assert.Null(state.Delivery.CompletedAt);
        clock.Now = clock.Now.AddMinutes(2);
        await Dispatcher().DispatchAsync(default);
        Assert.NotNull(state.Delivery.CompletedAt);
        Assert.Single(transport.Messages);
    }

    [Theory]
    [InlineData("opt-out")]
    [InlineData("logout")]
    [InlineData("account-switch")]
    [InlineData("deactivated")]
    public async Task PendingDeliveryRespectsCurrentRecipient(string change)
    {
        var state = await Queue();
        if (change == "opt-out") state.User.PushEnabled = false;
        if (change == "logout") state.Session.RevokedAt = clock.Now;
        if (change == "account-switch") state.Device.UserId = Guid.NewGuid();
        if (change == "deactivated") state.User.IsActive = false;
        await db.SaveChangesAsync();
        await Dispatcher().DispatchAsync(default);
        Assert.Empty(transport.Messages);
        Assert.Equal("recipient_unavailable", state.Delivery.FailureCode);
        Assert.NotNull(state.Delivery.CompletedAt);
    }

    [Fact]
    public async Task ReceiptFailuresExhaustBoundedRetries()
    {
        var state = await Queue();
        state.Delivery.TicketId = "existing-ticket";
        state.Delivery.Attempts = 14;
        transport.FailReceipts = true;
        await db.SaveChangesAsync();
        await Dispatcher().DispatchAsync(default);
        Assert.Equal(15, state.Delivery.Attempts);
        Assert.NotNull(state.Delivery.CompletedAt);
        Assert.Equal("push_transport_unavailable", state.Delivery.FailureCode);
    }

    [Fact]
    public async Task DuplicateCommittedEventDoesNotQueueTwice()
    {
        var state = await Queue();
        await new NotificationService(db, clock).EnqueueAsync(state.User.Id, "event-1", NotificationKind.ChargingCompleted, default);
        await db.SaveChangesAsync();
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Single(await db.PushDeliveries.ToListAsync());
    }

    public void Dispose() => db.Dispose();
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T17:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeTransport : IPushTransport
    {
        public List<PushMessage> Messages { get; } = [];
        public bool FailReceipts { get; set; }
        public Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(new PushResult(true, false, "ticket", null));
        }
        public Task<PushReceipt> CheckReceiptAsync(string id, CancellationToken ct) =>
            FailReceipts ? throw new HttpRequestException("offline test") : Task.FromResult(new PushReceipt(false, true, false, null));
    }
}

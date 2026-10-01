using EVCharging.Application.Common;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;

namespace EVCharging.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T17:00:00Z");

    [Fact]
    public void MaintenanceSurvivesDeviceHeartbeatAndArchiveCannotClaimAvailability()
    {
        var point = new ChargePoint();
        point.Report(PointState.Available, Now);
        point.Restrict(ManualRestriction.Maintenance, "Inspection");
        point.Report(PointState.Available, Now.AddSeconds(1));
        Assert.Equal(PointState.Maintenance, point.EffectiveState);
        Assert.False(point.IsAvailable(Now.AddSeconds(2), TimeSpan.FromMinutes(1)));
        point.Restrict(ManualRestriction.None, "Inspection complete");
        Assert.True(point.IsAvailable(Now.AddSeconds(2), TimeSpan.FromMinutes(1)));
        point.Archive(false);
        Assert.False(point.IsAvailable(Now.AddSeconds(2), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void OldReportsCannotRegressStateAndStaleDataIsNotAvailable()
    {
        var point = new ChargePoint();
        point.Report(PointState.Charging, Now);
        Assert.False(point.Report(PointState.Available, Now.AddSeconds(-1)));
        Assert.Equal(PointState.Charging, point.EffectiveState);
        Assert.Throws<InvalidOperationException>(() => point.Archive(false));
        point.Report(PointState.Available, Now.AddSeconds(1));
        Assert.False(point.IsAvailable(Now.AddHours(1), TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => point.Restrict(ManualRestriction.Maintenance, ""));
    }

    [Fact]
    public void EnergyDuplicatesCannotDoubleCostAndPriceIsFrozen()
    {
        var session = new ChargingSession(Guid.NewGuid(), .35m, "EUR", Now);
        Assert.True(session.Measure("m1", 20m, 7m, Now.AddMinutes(10)));
        Assert.False(session.Measure("m1", 30m, 7m, Now.AddMinutes(11)));
        Assert.Equal(7m, session.CalculatedCost);
        Assert.Equal(.35m, session.UnitPriceSnapshot);
        Assert.True(session.Complete("end", 20m, Now.AddMinutes(20)));
        Assert.False(session.Complete("end", 20m, Now.AddMinutes(20)));
        Assert.Throws<InvalidOperationException>(() =>
            session.Measure("late", 21m, null, Now.AddMinutes(21))
        );
    }

    [Fact]
    public void EnergyRegressionAndOutOfOrderAreHandled()
    {
        var session = new ChargingSession(Guid.NewGuid(), .35m, "EUR", Now);
        session.Measure("m1", 10m, null, Now.AddMinutes(5));
        Assert.False(session.Measure("old", 8m, null, Now.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            session.Measure("reset", 1m, null, Now.AddMinutes(6))
        );
        Assert.Null(session.PowerKW);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Measure("bad", -1m, null, Now.AddMinutes(7))
        );
    }

    [Theory]
    [InlineData("sq", AppLanguage.Sq)]
    [InlineData("en", AppLanguage.En)]
    public void SupportedLanguagesAreExplicit(string code, AppLanguage language) =>
        Assert.Equal(language, AccountRules.Language(code));

    [Fact]
    public void InvalidRegistrationInputCannotBecomeAnAccount()
    {
        Assert.Throws<RequestFailure>(() => AccountRules.Language("de"));
        Assert.Throws<RequestFailure>(() => AccountRules.ValidatePassword("short"));
        Assert.Throws<RequestFailure>(() => AccountRules.NormalizeEmail("not-an-email"));
        Assert.Throws<RequestFailure>(() => AccountRules.Name(" "));
        Assert.Equal("DRIVER@EXAMPLE.COM", AccountRules.NormalizeEmail(" driver@example.com "));
    }

    [Fact]
    public void EveryRequiredNotificationHasTwoTranslations()
    {
        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            var sq = NotificationTemplates.Get(kind, AppLanguage.Sq);
            var en = NotificationTemplates.Get(kind, AppLanguage.En);
            Assert.NotEmpty(sq.Title);
            Assert.NotEmpty(en.Body);
            Assert.NotEqual(sq.Title, en.Title);
        }
        Assert.Equal(
            "Karikimi filloi",
            NotificationTemplates.Get(NotificationKind.ChargingStarted, AppLanguage.Sq).Title
        );
    }
}

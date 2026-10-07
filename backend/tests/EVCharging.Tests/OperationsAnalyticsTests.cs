using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Tests;

public sealed class OperationsAnalyticsTests : IDisposable
{
    private readonly ApiFactory factory = new(clock: new AnalyticsClock());

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task EveryHistoricalPanelUsesTheSameAuthorizedCalendarAndCurrentValuesStayCurrent()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var seed = await AnalyticsTestData.CreateAsync(db);
        var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
        var day = await service.AnalyticsAsync(seed.AdminId, new(), default);
        Assert.Equal(new AnalyticsMetrics(3, 4, 8m, 2.80m, 1), day.Metrics);
        Assert.Equal(new AnalyticsMetrics(2, 1, 2m, .70m, 0), day.Comparison.Previous);
        Assert.Equal(50m, day.Comparison.SessionsPercent);
        Assert.Equal(300m, day.Comparison.EnergyPercent);
        Assert.Equal(300m, day.Comparison.CostPercent);
        Assert.Equal(day.Metrics.Sessions, day.Trend.Sum(x => x.Sessions ?? 0));
        Assert.Equal(day.Metrics.EnergyKWh, day.Trend.Sum(x => x.EnergyKWh ?? 0));
        Assert.Equal(day.Metrics.CalculatedCost, day.Trend.Sum(x => x.CalculatedCost ?? 0));
        Assert.Equal(day.Metrics.EnergyKWh, day.StationRanking.Sum(x => x.EnergyKWh));
        Assert.Equal(day.Metrics.EnergyKWh, Assert.Single(day.LocationRanking).EnergyKWh);
        Assert.Equal(day.Metrics.CompletedSessions, day.Outcomes.Sum(x => x.Sessions));
        Assert.Contains(day.Outcomes, x => x.Kind == "Interrupted" && x.Sessions == 1);
        Assert.Contains(day.Outcomes, x => x.Kind == "Unverified" && x.Sessions == 1);
        Assert.Equal(2, day.PeakUsage!.Sessions);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T07:00:00Z"), day.PeakUsage.From);
        Assert.DoesNotContain(day.StationRanking, x => x.Id == seed.HiddenStationId);
        Assert.Equal(1, day.Current.ActiveSessions);
        Assert.Equal(1, day.Current.Stations); // Archived station's history is retained, but it is not available now.
        Assert.Equal(1, day.Current.StatusCounts["Available"]);
        Assert.Equal(0, day.Current.Drivers);
        Assert.Contains(day.Trend, x => x.Sessions == null && x.EnergyKWh == null);
        var past = await service.AnalyticsAsync(
            seed.AdminId,
            new("Monthly", new DateOnly(2026, 9, 1)),
            default
        );
        Assert.Equal(new AnalyticsMetrics(0, 0, 0, 0, 0), past.Metrics);
        Assert.Empty(past.StationRanking);
        Assert.Empty(past.Outcomes);
        Assert.Null(past.PeakUsage);
        Assert.Equal(day.Current.Stations, past.Current.Stations);
        Assert.Equal(day.Current.ActiveSessions, past.Current.ActiveSessions);
        Assert.Equal(0m, past.Comparison.EnergyPercent);
        var all = await service.AnalyticsAsync(seed.SuperId, new(), default);
        Assert.Equal(108m, all.Metrics.EnergyKWh);
        Assert.Equal(2, all.Current.Stations);
    }

    [Fact]
    public async Task StandardAndCustomRangesUseCalendarPeriodsAndMissingPreviousActivityHasNoInfinitePercent()
    {
        using var scope = factory.Services.CreateScope();
        var seed = await AnalyticsTestData.CreateAsync(
            scope.ServiceProvider.GetRequiredService<ChargingDbContext>()
        );
        var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
        foreach (var kind in new[] { "Weekly", "Monthly", "Custom" })
        {
            var filter =
                kind == "Custom"
                    ? new AnalyticsFilter(kind, From: new(2026, 10, 5), Until: new(2026, 10, 7))
                    : new(kind);
            var result = await service.AnalyticsAsync(seed.AdminId, filter, default);
            Assert.Equal(6, result.Metrics.Sessions);
            Assert.Equal(10m, result.Metrics.EnergyKWh);
            Assert.Null(result.Comparison.SessionsPercent);
            Assert.Equal(result.Metrics.Sessions, result.Trend.Sum(x => x.Sessions ?? 0));
            Assert.Equal(result.Metrics.EnergyKWh, result.LocationRanking.Sum(x => x.EnergyKWh));
        }
        var previousDay = await service.AnalyticsAsync(
            seed.AdminId,
            new(Date: new(2026, 10, 6)),
            default
        );
        Assert.Equal(new AnalyticsMetrics(2, 1, 2m, .70m, 0), previousDay.Metrics);
        Assert.True(previousDay.Period.CanGoNext);
        Assert.DoesNotContain(previousDay.Trend, x => x.Sessions is null);
        var unassigned = new Account
        {
            Email = "empty@analytics.test",
            NormalizedEmail = "EMPTY@ANALYTICS.TEST",
            Role = UserRole.Admin,
        };
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        db.Accounts.Add(unassigned);
        await db.SaveChangesAsync();
        var empty = await service.AnalyticsAsync(unassigned.Id, new(), default);
        Assert.Equal(0, empty.Metrics.Sessions);
        Assert.Equal(0, empty.Current.Stations);
    }

    [Fact]
    public async Task DriverAndInactiveAccountsCannotReadAnalytics()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
        var driver = new Account
        {
            Email = "driver@analytics.test",
            NormalizedEmail = "DRIVER@ANALYTICS.TEST",
            Role = UserRole.Driver,
        };
        var inactive = new Account
        {
            Email = "inactive@analytics.test",
            NormalizedEmail = "INACTIVE@ANALYTICS.TEST",
            Role = UserRole.Admin,
            IsActive = false,
        };
        db.Accounts.AddRange(driver, inactive);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IOperationsManagement>();
        foreach (var id in new[] { driver.Id, inactive.Id, Guid.NewGuid() })
            Assert.Equal(
                403,
                (
                    await Assert.ThrowsAsync<RequestFailure>(() =>
                        service.AnalyticsAsync(id, new(), default)
                    )
                ).Status
            );
    }

    private sealed class AnalyticsClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-07T10:00:00Z");
    }
}

internal sealed record AnalyticsTestData(Guid SuperId, Guid AdminId, Guid HiddenStationId)
{
    public static async Task<AnalyticsTestData> CreateAsync(ChargingDbContext db)
    {
        var super = new Account
        {
            Email = "super@analytics.test",
            NormalizedEmail = "SUPER@ANALYTICS.TEST",
            Role = UserRole.Superadmin,
        };
        var admin = new Account
        {
            Email = "admin@analytics.test",
            NormalizedEmail = "ADMIN@ANALYTICS.TEST",
            Role = UserRole.Admin,
        };
        var location = new ChargingLocation { Name = "Assigned depot" };
        var assigned = new ChargingStation
        {
            Code = "AN-ASSIGNED",
            Name = "Assigned station",
            LocationId = location.Id,
            ConnectorCount = 2,
            ConnectorType = "CCS2",
            PowerKW = 50,
        };
        var hidden = new ChargingStation
        {
            Code = "AN-HIDDEN",
            Name = "Hidden station",
            ConnectorCount = 1,
            ConnectorType = "CCS2",
            PowerKW = 50,
        };
        var archived = new ChargingStation
        {
            Code = "AN-ARCHIVED",
            Name = "Archived station",
            LocationId = location.Id,
            ConnectorCount = 1,
            ConnectorType = "CCS2",
            PowerKW = 50,
            IsArchived = true,
        };
        var connectors = new[]
        {
            new ChargingConnector { StationId = assigned.Id, Number = 1 },
            new ChargingConnector { StationId = assigned.Id, Number = 2 },
            new ChargingConnector { StationId = hidden.Id, Number = 1 },
            new ChargingConnector { StationId = archived.Id, Number = 1 },
        };
        db.Accounts.AddRange(super, admin);
        db.Locations.Add(location);
        db.Stations.AddRange(assigned, hidden, archived);
        db.ChargingConnectors.AddRange(connectors);
        db.AdministratorGrants.Add(new() { AdministratorId = admin.Id, LocationId = location.Id });
        ChargingSession Session(
            int connector,
            string start,
            string? end,
            decimal? energy,
            string? reason = null,
            string? warning = null
        ) =>
            new()
            {
                PointId = connectors[connector].StationId,
                ConnectorId = connectors[connector].Id,
                ConnectorNumber = connectors[connector].Number,
                StartedAt = DateTimeOffset.Parse(start),
                StartReceivedAt = DateTimeOffset.Parse(start),
                LastMeasurementAt = DateTimeOffset.Parse(start),
                EndedAt = end is null ? null : DateTimeOffset.Parse(end),
                StopReceivedAt = end is null ? null : DateTimeOffset.Parse(end),
                EnergyKWh = energy,
                UnitPriceSnapshot = .35m,
                Status = end is null ? ChargingState.Charging : ChargingState.Completed,
                StopReason = reason,
                CompletionWarning = warning,
            };
        db.ChargingSessions.AddRange(
            Session(0, "2026-10-06T08:00:00Z", "2026-10-06T09:00:00Z", 2),
            Session(0, "2026-10-06T21:00:00Z", "2026-10-06T22:00:00Z", 3),
            Session(0, "2026-10-07T07:00:00Z", "2026-10-07T08:00:00Z", 4, "EmergencyStop"),
            Session(0, "2026-10-07T07:30:00Z", "2026-10-07T09:00:00Z", null, warning: null),
            Session(1, "2026-10-05T05:00:00Z", null, 5),
            Session(0, "2026-10-07T22:00:00Z", "2026-10-07T23:00:00Z", 20),
            Session(2, "2026-10-07T07:00:00Z", "2026-10-07T08:00:00Z", 100),
            Session(3, "2026-10-07T03:00:00Z", "2026-10-07T04:00:00Z", 1)
        );
        await db.SaveChangesAsync();
        return new(super.Id, admin.Id, hidden.Id);
    }
}

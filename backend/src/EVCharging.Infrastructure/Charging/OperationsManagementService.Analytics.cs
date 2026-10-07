using System.Data;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService
{
    public async Task<OperationsAnalytics> AnalyticsAsync(
        Guid actorId,
        AnalyticsFilter filter,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        var now = clock.GetUtcNow();
        var calendar = new AnalyticsCalendar(filter, now);
        var period = calendar.Period;
        // One database snapshot keeps KPI, bucket and ranking totals consistent
        // when a charger finishes a session during this request.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            : null;
        var stations = access.Stations(actor).AsNoTracking();
        var stationIds = stations.Select(x => x.Id);
        var sessions = db
            .ChargingSessions.AsNoTracking()
            .Where(x => stationIds.Contains(x.PointId));
        var from = period.From;
        var until = period.EffectiveUntil;
        var metrics = await AnalyticsMetricsAsync(sessions, from, until, ct);
        var previous = await AnalyticsMetricsAsync(
            sessions,
            period.PreviousFrom,
            period.PreviousUntil,
            ct
        );
        var starts = await sessions
            .Where(x => x.StartedAt >= from && x.StartedAt < until)
            .GroupBy(calendar.BucketKey(false))
            .Select(g => new { Index = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Index, x => x.Count, ct);
        var completions = sessions.Where(x => x.EndedAt >= from && x.EndedAt < until);
        var endings = await completions
            .GroupBy(calendar.BucketKey(true))
            .Select(g => new
            {
                Index = g.Key,
                Count = g.Count(),
                Energy = g.Sum(x => x.EnergyKWh ?? 0),
                Cost = g.Sum(x =>
                    decimal.Floor((x.EnergyKWh ?? 0) * x.UnitPriceSnapshot * 100 + 0.5m) / 100
                ),
            })
            .ToDictionaryAsync(x => x.Index, ct);
        var trend = calendar
            .Buckets.Select(
                (bucket, index) =>
                {
                    var elapsed = bucket.From < until;
                    endings.TryGetValue(index, out var ending);
                    return new AnalyticsTrendPoint(
                        bucket.From,
                        bucket.Until,
                        elapsed ? starts.GetValueOrDefault(index) : null,
                        elapsed ? ending?.Count ?? 0 : null,
                        elapsed ? ending?.Energy ?? 0 : null,
                        elapsed ? ending?.Cost ?? 0 : null
                    );
                }
            )
            .ToArray();

        var selected = sessions.Where(x =>
            x.StartedAt >= from && x.StartedAt < until || x.EndedAt >= from && x.EndedAt < until
        );
        var joined =
            from session in selected
            join station in stations on session.PointId equals station.Id
            select new { Session = session, Station = station };
        var stationTotals = await joined
            .GroupBy(x => new
            {
                x.Station.Id,
                x.Station.Name,
                x.Station.Code,
            })
            .Select(g => new
            {
                g.Key.Id,
                g.Key.Name,
                g.Key.Code,
                Sessions = g.Count(x => x.Session.StartedAt >= from && x.Session.StartedAt < until),
                Completed = g.Count(x => x.Session.EndedAt >= from && x.Session.EndedAt < until),
                Energy = g.Sum(x =>
                    x.Session.EndedAt >= from && x.Session.EndedAt < until
                        ? x.Session.EnergyKWh ?? 0
                        : 0
                ),
                Cost = g.Sum(x =>
                    x.Session.EndedAt >= from && x.Session.EndedAt < until
                        ? decimal.Floor(
                            (x.Session.EnergyKWh ?? 0) * x.Session.UnitPriceSnapshot * 100 + 0.5m
                        ) / 100
                        : 0
                ),
            })
            .OrderByDescending(x => x.Energy)
            .ThenByDescending(x => x.Sessions)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToListAsync(ct);
        var stationRanking = stationTotals
            .Select(x => new AnalyticsRanking(
                x.Id,
                x.Name,
                x.Code,
                x.Sessions,
                x.Completed,
                x.Energy,
                x.Cost
            ))
            .ToArray();
        var locationTotals = await joined
            .GroupBy(x => x.Station.LocationId)
            .Select(g => new
            {
                Id = g.Key,
                Sessions = g.Count(x => x.Session.StartedAt >= from && x.Session.StartedAt < until),
                Completed = g.Count(x => x.Session.EndedAt >= from && x.Session.EndedAt < until),
                Energy = g.Sum(x =>
                    x.Session.EndedAt >= from && x.Session.EndedAt < until
                        ? x.Session.EnergyKWh ?? 0
                        : 0
                ),
                Cost = g.Sum(x =>
                    x.Session.EndedAt >= from && x.Session.EndedAt < until
                        ? decimal.Floor(
                            (x.Session.EnergyKWh ?? 0) * x.Session.UnitPriceSnapshot * 100 + 0.5m
                        ) / 100
                        : 0
                ),
            })
            .OrderByDescending(x => x.Energy)
            .ThenByDescending(x => x.Sessions)
            .ThenBy(x => x.Id)
            .Take(10)
            .ToListAsync(ct);
        var locationIds = locationTotals
            .Where(x => x.Id != null)
            .Select(x => x.Id!.Value)
            .ToArray();
        var locationNames = await db
            .Locations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var locationRanking = locationTotals
            .Select(x => new AnalyticsRanking(
                x.Id,
                x.Id is { } id
                    ? locationNames.GetValueOrDefault(id, "Unknown location")
                    : "Unassigned",
                null,
                x.Sessions,
                x.Completed,
                x.Energy,
                x.Cost
            ))
            .ToArray();
        var interrupted = new[]
        {
            "EmergencyStop",
            "PowerLoss",
            "HardReset",
            "SoftReset",
            "Reboot",
            "DeAuthorized",
            "Other",
        };
        var outcomes = await completions
            .GroupBy(x =>
                x.CompletionWarning != null || x.EnergyKWh == null ? "Unverified"
                : interrupted.Contains(x.StopReason!) ? "Interrupted"
                : "Completed"
            )
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var outcomeDtos = outcomes
            .OrderBy(x => x.Kind)
            .Select(x => new AnalyticsOutcome(
                x.Kind,
                x.Count,
                metrics.CompletedSessions == 0
                    ? 0
                    : decimal.Round(100m * x.Count / metrics.CompletedSessions, 1)
            ))
            .ToArray();
        var peak = trend
            .Where(x => x.Sessions > 0)
            .OrderByDescending(x => x.Sessions)
            .ThenBy(x => x.From)
            .FirstOrDefault();

        // This section deliberately ignores the historical period.
        var status = await stations
            .Where(x => !x.IsArchived)
            .GroupBy(x => x.Status)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var current = new OperationsCurrent(
            status.Sum(x => x.Count),
            Enum.GetValues<PointState>()
                .ToDictionary(
                    x => x.ToString(),
                    x => status.Where(g => g.State == x).Sum(g => g.Count)
                ),
            await access.Locations(actor).CountAsync(x => !x.IsArchived, ct),
            actor.Role == UserRole.Superadmin
                ? await db.Accounts.CountAsync(
                    x => x.Role == UserRole.Admin && x.IsActive && x.ArchivedAt == null,
                    ct
                )
                : 0,
            actor.Role == UserRole.Superadmin
                ? await db.Accounts.CountAsync(
                    x => x.Role == UserRole.Driver && x.IsActive && x.ArchivedAt == null,
                    ct
                )
                : 0,
            await sessions.CountAsync(x => x.EndedAt == null, ct),
            now
        );
        var result = new OperationsAnalytics(
            period,
            current,
            metrics,
            new(
                previous,
                AnalyticsPercent(metrics.Sessions, previous.Sessions),
                AnalyticsPercent(metrics.EnergyKWh, previous.EnergyKWh),
                AnalyticsPercent(metrics.CalculatedCost, previous.CalculatedCost)
            ),
            trend,
            stationRanking,
            locationRanking,
            outcomeDtos,
            peak is null ? null : new(peak.From, peak.Until, peak.Sessions!.Value),
            "EUR",
            "SessionStartsAndCompletedSessionEnergy",
            now
        );
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<AnalyticsMetrics> AnalyticsMetricsAsync(
        IQueryable<ChargingSession> sessions,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken ct
    ) =>
        await sessions
            .Where(x =>
                x.StartedAt >= from && x.StartedAt < until || x.EndedAt >= from && x.EndedAt < until
            )
            .GroupBy(_ => 1)
            .Select(g => new AnalyticsMetrics(
                g.Count(x => x.StartedAt >= from && x.StartedAt < until),
                g.Count(x => x.EndedAt >= from && x.EndedAt < until),
                g.Sum(x => x.EndedAt >= from && x.EndedAt < until ? x.EnergyKWh ?? 0 : 0),
                g.Sum(x =>
                    x.EndedAt >= from && x.EndedAt < until
                        ? decimal.Floor((x.EnergyKWh ?? 0) * x.UnitPriceSnapshot * 100 + 0.5m) / 100
                        : 0
                ),
                g.Count(x => x.EndedAt >= from && x.EndedAt < until && x.EnergyKWh == null)
            ))
            .SingleOrDefaultAsync(ct)
        ?? new(0, 0, 0, 0, 0);

    private static decimal? AnalyticsPercent(decimal current, decimal previous) =>
        previous == 0
            ? current == 0
                ? 0
                : null
            : decimal.Round((current - previous) / previous * 100, 1);
}

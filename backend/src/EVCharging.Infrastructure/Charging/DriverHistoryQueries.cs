using System.Data;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class DriverHistoryQueries(ChargingDbContext db, IOptions<RuntimeFeatures> features)
    : IDriverHistoryQueries
{
    public async Task<DriverHistoryPage> SearchAsync(
        Guid driverId,
        DriverHistoryFilter filter,
        CancellationToken ct
    )
    {
        DriverHistoryCalendar.Validate(filter);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            : null;
        var own = db
            .ChargingHistory.AsNoTracking()
            .Where(x =>
                x.DriverId == driverId
                && (features.Value.TestMode || !x.IsDemo && !x.Station.IsDemo)
            );
        var bounds = await own.GroupBy(x => 1)
            .Select(g => new { From = g.Min(x => x.EndedAt), Until = g.Max(x => x.EndedAt) })
            .SingleOrDefaultAsync(ct);
        if (bounds is null)
            return new(
                [],
                0,
                1,
                filter.PageSize,
                1,
                0,
                null,
                null,
                new(0, 0, new Dictionary<string, decimal>()),
                new("day", []),
                [],
                [],
                false
            );
        var calendar = new DriverHistoryCalendar(filter, DriverHistoryCalendar.Day(bounds.Until));
        var from = DriverHistoryCalendar.Midnight(calendar.Range.From);
        var until = DriverHistoryCalendar.Midnight(calendar.Range.Until.AddDays(1));
        var query = own.Where(x => x.EndedAt >= from && x.EndedAt < until);
        if (filter.StationId is { } stationId)
            query = query.Where(x => x.StationId == stationId);
        if (filter.Currency is { } currency)
            query = query.Where(x => x.Currency == currency);
        query = filter.Energy switch
        {
            "small" => query.Where(x => x.EnergyKWh <= 10),
            "medium" => query.Where(x => x.EnergyKWh > 10 && x.EnergyKWh <= 30),
            "large" => query.Where(x => x.EnergyKWh > 30),
            _ => query,
        };
        var totals = await query
            .Select(calendar.Projection())
            .GroupBy(x => new { x.Bucket, x.Currency })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.Currency,
                Charges = g.Count(),
                Energy = g.Sum(x => x.EnergyKWh),
                Cost = g.Sum(x => x.TotalCost),
                Demo = g.Count(x => x.IsDemo),
            })
            .ToListAsync(ct);
        var buckets = calendar
            .Buckets.Select(
                (range, index) =>
                {
                    var values = totals.Where(x => x.Bucket == index).ToArray();
                    return new HistoryBucket(
                        range.From.ToString("yyyy-MM-dd"),
                        range.From.ToString("yyyy-MM-dd") + "T12:00:00Z",
                        range.From,
                        range.Until,
                        values.Sum(x => x.Charges),
                        values.Sum(x => x.Energy),
                        values.ToDictionary(x => x.Currency, x => x.Cost)
                    );
                }
            )
            .ToArray();
        var selected = filter.Bucket is null
            ? buckets
            : buckets.Where(x => x.From == filter.Bucket).ToArray();
        if (filter.Bucket is not null && selected.Length == 0)
            throw DriverHistoryCalendar.Invalid();
        var summary = new HistorySummary(
            selected.Sum(x => x.Charges),
            selected.Sum(x => x.EnergyKWh),
            selected
                .SelectMany(x => x.Costs)
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Value))
        );
        if (filter.Bucket is not null)
        {
            var start = DriverHistoryCalendar.Midnight(selected[0].From);
            var end = DriverHistoryCalendar.Midnight(selected[0].Until.AddDays(1));
            query = query.Where(x => x.EndedAt >= start && x.EndedAt < end);
        }
        var pages = Math.Max(1, (int)Math.Ceiling(summary.Charges / (decimal)filter.PageSize));
        var page = Math.Min(filter.Page, pages);
        var offset = (page - 1) * filter.PageSize;
        var items = await query
            .OrderByDescending(x => x.EndedAt)
            .ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(filter.PageSize)
            .Select(x => new DriverHistoryDto(
                x.Id,
                x.StartedAt,
                x.EndedAt,
                x.EnergyKWh,
                x.UnitPriceSnapshot,
                x.TotalCost,
                x.Currency,
                x.IsDemo,
                x.StationId,
                x.Station.Code,
                x.Station.Name,
                x.Station.City
            ))
            .ToListAsync(ct);
        var stations = await own.Select(x => new
            {
                x.StationId,
                x.Station.Name,
                x.Station.Code,
            })
            .Distinct()
            .OrderBy(x => x.Name)
            .Select(x => new HistoryStation(x.StationId, x.Name, x.Code))
            .ToListAsync(ct);
        var currencies = await own.Select(x => x.Currency)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return new(
            items,
            summary.Charges,
            page,
            filter.PageSize,
            pages,
            offset,
            new(DriverHistoryCalendar.Day(bounds.From), DriverHistoryCalendar.Day(bounds.Until)),
            calendar.Range,
            summary,
            new(calendar.Granularity, buckets),
            stations,
            currencies,
            totals.Any(x => x.Demo > 0)
        );
    }
}

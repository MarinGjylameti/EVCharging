using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class ApplicationLogStore(
    ChargingDbContext db,
    ApplicationDiagnostics diagnostics,
    IOptions<DiagnosticOptions> options,
    TimeProvider clock
) : IApplicationLogHistory
{
    public async Task PersistAsync(IReadOnlyList<ApplicationLogEntry> batch, CancellationToken ct)
    {
        var ids = batch.Select(x => x.EventId).ToArray();
        // Recover safely if the previous commit succeeded but its acknowledgement was lost.
        var existing = await db
            .ApplicationLogs.Where(x => ids.Contains(x.EventId))
            .Select(x => x.EventId)
            .ToListAsync(ct);
        db.ApplicationLogs.AddRange(
            batch
                .Where(x => !existing.Contains(x.EventId))
                .Select(x => new ApplicationLogRecord
                {
                    EventId = x.EventId,
                    InstanceId = x.InstanceId!,
                    Sequence = x.Sequence,
                    RecordedAt = x.RecordedAt,
                    Source = x.Source,
                    Level = x.Level,
                    Message = x.Message,
                    Identity = x.Identity,
                    TraceId = x.Context?.TraceId,
                    ConnectionId = x.Context?.ConnectionId,
                    ContextJson = JsonSerializer.Serialize(x.Context ?? new()),
                })
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationLogHistory> ReadAsync(
        ApplicationLogFilter filter,
        CancellationToken ct
    )
    {
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.RetentionDays);
        var query = db.ApplicationLogs.AsNoTracking().Where(x => x.RecordedAt >= cutoff);
        if (filter.Source is { } source)
            query = query.Where(x => x.Source == source);
        if (filter.Identity is { } identity)
        {
            var lower = identity.ToLowerInvariant();
            query = query.Where(x => x.Identity != null && x.Identity.ToLower().Contains(lower));
        }
        if (filter.Level is { } level)
            query = query.Where(x => x.Level == level);
        if (filter.TraceId is { } trace)
            query = query.Where(x => x.TraceId == trace);
        if (filter.ConnectionId is { } connection)
            query = query.Where(x => x.ConnectionId == connection);
        if (filter.From is { } from)
            query = query.Where(x => x.RecordedAt >= from.ToUniversalTime());
        if (filter.Until is { } until)
            query = query.Where(x => x.RecordedAt <= until.ToUniversalTime());
        if (filter.BeforeId is { } before)
            query = query.Where(x => x.Id < before);
        var rows = await query.OrderByDescending(x => x.Id).Take(filter.Limit + 1).ToListAsync(ct);
        var entries = rows.Take(filter.Limit)
            .Select(x => new ApplicationLogEntry(
                x.Sequence,
                x.RecordedAt,
                x.Source,
                x.Level,
                x.Message,
                x.Identity,
                x.EventId,
                x.InstanceId,
                JsonSerializer.Deserialize<ApplicationLogContext>(x.ContextJson),
                x.Id
            ))
            .ToArray();
        return new(
            entries,
            rows.Count > filter.Limit ? entries[^1].StorageId : null,
            options.Value.RetentionDays,
            diagnostics.StorageStatus()
        );
    }

    public async Task PruneAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.RetentionDays);
        // Bound each deletion so retention cannot monopolize the production database.
        var expired = db
            .ApplicationLogs.Where(x => x.RecordedAt < cutoff)
            .OrderBy(x => x.Id)
            .Take(2000);
        if (db.Database.IsRelational())
            await expired.ExecuteDeleteAsync(ct);
        else
        {
            db.ApplicationLogs.RemoveRange(await expired.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
    }
}

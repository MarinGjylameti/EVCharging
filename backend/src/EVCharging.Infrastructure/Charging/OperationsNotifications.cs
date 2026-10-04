using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class OperationsNotifications(ChargingDbContext db, NotificationService notifications)
{
    public async Task EnqueueAsync(
        Guid stationId,
        string source,
        NotificationKind kind,
        CancellationToken ct,
        Guid? driverId = null
    )
    {
        var station =
            db.Stations.Local.SingleOrDefault(x => x.Id == stationId)
            ?? await db.Stations.SingleAsync(x => x.Id == stationId, ct);
        var recipients = await db
            .Accounts.Where(a =>
                a.IsActive
                && a.ArchivedAt == null
                && (
                    a.Role == UserRole.Superadmin
                    || a.Role == UserRole.Admin
                        && db.AdministratorGrants.Any(g =>
                            g.AdministratorId == a.Id
                            && (
                                g.StationId == stationId
                                || station.LocationId != null && g.LocationId == station.LocationId
                            )
                        )
                )
            )
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (driverId is { } owner)
            recipients.Add(owner);
        else
        {
            recipients.AddRange(
                db.ChargingConnectionRequests.Local.Where(c =>
                        c.StationId == stationId && c.State == "Linked"
                    )
                    .Select(c => c.DriverId)
            );
            recipients.AddRange(
                await db
                    .ChargingConnectionRequests.Where(c =>
                        c.StationId == stationId && c.State == "Linked"
                    )
                    .Select(c => c.DriverId)
                    .ToListAsync(ct)
            );
        }
        foreach (var id in recipients.Distinct())
            await notifications.EnqueueAsync(id, source, kind, ct);
    }
}

using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EVCharging.Infrastructure.Charging;

internal static class StationMutationLock
{
    public static async Task<IDbContextTransaction?> BeginAsync(
        ChargingDbContext db,
        Guid stationId,
        CancellationToken ct
    )
    {
        if (!db.Database.IsRelational())
            return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Gateway/claim changes lock devices first. Keep that order and sort
            // multiple devices before taking the station lock to avoid deadlocks.
            var devices = await db
                .OcppConnectors.Where(x => x.StationId == stationId)
                .Select(x => x.ChargePointId)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);
            foreach (var device in devices)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM ocpp_charge_points WHERE id = {device} FOR UPDATE",
                    ct
                );
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM charging_stations WHERE id = {stationId} FOR UPDATE",
                ct
            );
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}

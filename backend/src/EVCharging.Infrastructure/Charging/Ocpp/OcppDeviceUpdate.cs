using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EVCharging.Infrastructure.Charging.Ocpp;

internal static class OcppDeviceUpdate
{
    public static async Task<IDbContextTransaction?> BeginAsync(
        ChargingDbContext db,
        Guid deviceId,
        CancellationToken ct
    )
    {
        if (!db.Database.IsRelational())
            return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM ocpp_charge_points WHERE id = {deviceId} FOR UPDATE",
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

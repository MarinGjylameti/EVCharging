using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EVCharging.Infrastructure.Charging;

internal static class LocationMutationLock
{
    public static async Task<IDbContextTransaction?> BeginAsync(
        ChargingDbContext db,
        Guid? locationId,
        CancellationToken ct
    )
    {
        if (!db.Database.IsRelational())
            return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (locationId is { } id)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM charging_locations WHERE id = {id} FOR UPDATE",
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

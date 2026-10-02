using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EVCharging.Infrastructure.Charging;

// Adapter mapping is confined to Infrastructure. Claim/session code uses only
// business connector identities. Another adapter supplies its lock mapping here.
internal static class ConnectorMutationLock
{
    public static async Task<IDbContextTransaction?> BeginAsync(
        ChargingDbContext db,
        Guid connectorId,
        Guid driverId,
        CancellationToken ct
    )
    {
        var deviceId = await db
            .OcppConnectors.Where(x => x.BusinessConnectorId == connectorId)
            .Select(x => x.ChargePointId)
            .SingleAsync(ct);
        var transaction = await OcppDeviceUpdate.BeginAsync(db, deviceId, ct);
        if (transaction is null)
            return null;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM accounts WHERE id = {driverId} FOR UPDATE",
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

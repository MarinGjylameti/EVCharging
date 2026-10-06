using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Hardware telemetry can arrive before an operator associates a public station.
// Discovered connectors stay private until an explicit business mapping is made.
public sealed class OcppHardwareDiscovery(ChargingDbContext db)
{
    public async Task<OcppConnector> ConnectorAsync(Guid deviceId, int number, CancellationToken ct)
    {
        var connector = await db.OcppConnectors.SingleOrDefaultAsync(
            x => x.ChargePointId == deviceId && x.ConnectorNumber == number,
            ct
        );
        if (connector is not null)
            return connector;
        var physical = await db.OcppChargePoints.AnyAsync(
            x => x.Id == deviceId && x.ProvisionedSerialNumber != null,
            ct
        );
        if (!physical)
            throw new OcppFault("PropertyConstraintViolation", "Connector is not provisioned.");
        connector = new OcppConnector { ChargePointId = deviceId, ConnectorNumber = number };
        db.OcppConnectors.Add(connector);
        // Runs inside the gateway's device lock/transaction.
        await db.SaveChangesAsync(ct);
        return connector;
    }
}

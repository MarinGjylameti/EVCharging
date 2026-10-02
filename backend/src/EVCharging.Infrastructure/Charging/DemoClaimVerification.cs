using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging;

public sealed class DemoClaimVerification(ChargingDbContext db, IOptions<OcppOptions> options)
    : IChargingClaimVerification
{
    public bool DemoBypassEnabled => options.Value.SimulatorEnabled;

    public async Task<ChargingConnector> VerifyAsync(Guid stationId, CancellationToken ct)
    {
        if (
            !DemoBypassEnabled
            || !await db.Stations.AnyAsync(x => x.Id == stationId && x.IsDemo && !x.IsArchived, ct)
        )
            throw new RequestFailure(
                403,
                "test_connection_unavailable",
                "Verified charger evidence is required."
            );
        return await db.ChargingConnectors.SingleOrDefaultAsync(
                x => x.StationId == stationId && x.Number == 1,
                ct
            ) ?? throw new RequestFailure(404, "connector_not_found", "Connector not found.");
    }
}

using System.Security.Cryptography;
using System.Text;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Explicit demo policy. Real charger credentials/profiles require a separate
// implementation once the hardware contract is supplied; disabled by default.
public sealed class OcppDeviceAuthentication(
    ChargingDbContext db,
    OcppSimulatorProvisioner provisioner,
    IOptions<OcppOptions> options
)
{
    public async Task<Guid?> AuthenticateAsync(
        string identity,
        string username,
        string password,
        CancellationToken ct
    )
    {
        var settings = options.Value;
        if (
            !settings.SimulatorEnabled
            || !settings.SimulatorIdentities.Contains(identity, StringComparer.Ordinal)
            || !System.Text.RegularExpressions.Regex.IsMatch(identity, "^[A-Za-z0-9_-]{1,40}$")
        )
            return null;
        var anonymousDemo =
            username.Length == 0
            && password.Length == 0
            && settings.AllowAnonymousSimulators
            && settings.SimulatorIdentities.Contains(identity, StringComparer.Ordinal);
        var authenticatedDemo =
            username == identity
            && settings.SimulatorPassword.Length >= 12
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(password)),
                SHA256.HashData(Encoding.UTF8.GetBytes(settings.SimulatorPassword))
            );
        if (!anonymousDemo && !authenticatedDemo)
            return null;
        var device = await db
            .OcppChargePoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Identity == identity, ct);
        if (device is not null)
        {
            var mapped = await db
                .OcppConnectors.AsNoTracking()
                .Where(x => x.ChargePointId == device.Id)
                .ToListAsync(ct);
            var stationIds = mapped.Select(x => x.StationId).ToArray();
            var demoStations = await db.Stations.CountAsync(
                x => stationIds.Contains(x.Id) && x.IsDemo && !x.IsArchived,
                ct
            );
            return
                device.IsEnabled
                && mapped.Count > 0
                && demoStations == stationIds.Distinct().Count()
                ? device.Id
                : null;
        }
        return (await provisioner.EnsureSimulatorAsync(identity, ct)).Id;
    }
}

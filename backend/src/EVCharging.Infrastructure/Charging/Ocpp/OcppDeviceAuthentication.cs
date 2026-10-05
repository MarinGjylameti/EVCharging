using System.Security.Cryptography;
using System.Text;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Physical devices connect by their provisioned URL identity and explicit connector mappings.
// The allowlisted demo policy stays independent of physical charger admission.
public sealed class OcppDeviceAuthentication(
    ChargingDbContext db,
    OcppSimulatorProvisioner provisioner,
    IOptions<OcppOptions> options
)
{
    public long? AuthenticatedCredentialVersion { get; private set; }

    public async Task<Guid?> AuthenticateAsync(
        string identity,
        string username,
        string password,
        CancellationToken ct
    )
    {
        var settings = options.Value;
        AuthenticatedCredentialVersion = null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(identity, "^[A-Za-z0-9_-]{1,40}$"))
            return null;
        var physical = await db
            .OcppChargePoints.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Identity == identity && x.ProvisionedSerialNumber != null,
                ct
            );
        if (physical is not null)
        {
            if (!physical.IsEnabled || physical.ProtocolVersion != "1.6J")
                return null;
            var mappings = await db
                .OcppConnectors.AsNoTracking()
                .Where(x => x.ChargePointId == physical.Id)
                .ToListAsync(ct);
            var stationIds = mappings.Select(x => x.StationId).Distinct().ToArray();
            if (
                mappings.Count == 0
                || mappings.Any(x => x.BusinessConnectorId == null || x.StationId == null)
                || await db.Stations.CountAsync(
                    x => stationIds.Contains(x.Id) && !x.IsArchived && !x.IsDemo,
                    ct
                ) != stationIds.Length
            )
                return null;
            AuthenticatedCredentialVersion = physical.CredentialVersion;
            return physical.Id;
        }
        if (
            !settings.SimulatorEnabled
            || !settings.SimulatorIdentities.Contains(identity, StringComparer.Ordinal)
            || password.Length > 128
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
            AuthenticatedCredentialVersion = device.CredentialVersion;
            return
                device.IsEnabled
                && mapped.Count > 0
                && demoStations == stationIds.Distinct().Count()
                ? device.Id
                : null;
        }
        var created = await provisioner.EnsureSimulatorAsync(identity, ct);
        AuthenticatedCredentialVersion = created.CredentialVersion;
        return created.Id;
    }
}

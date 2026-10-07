using System.Security.Cryptography;
using System.Text;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Physical devices are discovered by their URL identity; station mapping can happen later.
// The allowlisted demo policy stays independent of physical charger admission.
public sealed class OcppDeviceAuthentication(
    ChargingDbContext db,
    OcppSimulatorProvisioner provisioner,
    IOptions<OcppOptions> options,
    ILoggerFactory logs
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
        if (!OcppPath.IsValidIdentity(identity))
            return null;
        var physical = await db
            .OcppChargePoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Identity == identity, ct);
        var simulatorIdentity =
            settings.SimulatorIdentities.Contains(identity, StringComparer.Ordinal)
            || identity.StartsWith("SIM-", StringComparison.Ordinal);
        if (physical?.ProvisionedSerialNumber is not null || !simulatorIdentity)
        {
            if (physical is null)
            {
                physical = new OcppChargePoint
                {
                    Identity = identity,
                    ProvisionedSerialNumber = identity,
                    IsEnabled = true,
                    CredentialVersion = 1,
                    ManagementVersion = 1,
                };
                db.OcppChargePoints.Add(physical);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // A concurrent first connection or operator registration may win.
                    db.Entry(physical).State = EntityState.Detached;
                    physical = await db
                        .OcppChargePoints.AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Identity == identity, ct);
                    if (physical is null)
                        throw;
                }
                logs.CreateLogger("OcppWebSocket")
                    .LogInformation(
                        "OCPP device {Identity} discovered automatically; station association can follow.",
                        identity
                    );
            }
            else if (physical.ProvisionedSerialNumber is null)
            {
                var stored = await db.OcppChargePoints.SingleAsync(x => x.Id == physical.Id, ct);
                stored.ProvisionedSerialNumber = identity;
                await db.SaveChangesAsync(ct);
                physical = stored;
            }
            if (!physical.IsEnabled || physical.ProtocolVersion != "1.6J")
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

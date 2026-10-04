using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService
{
    private async Task<OcppChargePoint?> MappedDeviceAsync(Guid stationId, CancellationToken ct) =>
        await db.OcppChargePoints.SingleOrDefaultAsync(
            d => db.OcppConnectors.Any(c => c.ChargePointId == d.Id && c.StationId == stationId),
            ct
        );

    private async Task<DeviceDto> DeviceDetailsAsync(
        OcppChargePoint device,
        CancellationToken ct
    ) =>
        new(
            device.Id,
            device.Identity,
            device.ProvisionedSerialNumber ?? device.ChargePointSerialNumber ?? "",
            device.Vendor,
            device.Model,
            device.ProtocolVersion,
            device.IsEnabled,
            device.IsEnabled
                && device.LastSeenAt > clock.GetUtcNow().AddSeconds(120)
                && await db.OcppConnections.AnyAsync(
                    c => c.ChargePointId == device.Id && c.DisconnectedAt == null,
                    ct
                ),
            device.LastSeenAt,
            device.LastBootAt,
            await db
                .OcppConnectors.Where(c => c.ChargePointId == device.Id)
                .OrderBy(c => c.ConnectorNumber)
                .Select(c => c.ConnectorNumber)
                .ToArrayAsync(ct),
            DeviceTagAuthorization.Hashes(device).Length,
            device.ManagementVersion
        );

    public async Task<DeviceDto?> DeviceAsync(Guid actorId, Guid stationId, CancellationToken ct)
    {
        var actor = await access.ActorAsync(actorId, ct);
        await access.StationAsync(actor, stationId, ct);
        var device = await MappedDeviceAsync(stationId, ct);
        return device is null ? null : await DeviceDetailsAsync(device, ct);
    }

    public async Task<DeviceCredentials> ProvisionDeviceAsync(
        Guid actorId,
        Guid stationId,
        DeviceInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        if (input.Protocol != "1.6J")
            throw new RequestFailure(
                400,
                "unsupported_device_protocol",
                "Only OCPP 1.6 JSON is supported."
            );
        if (
            input.Identity is null
            || !Regex.IsMatch(input.Identity, "^[A-Za-z0-9_-]{2,40}$")
            || input.SerialNumber?.Trim().Length is not (>= 1 and <= 64)
            || input.Vendor?.Length > 64
            || input.Model?.Length > 64
            || input.AuthorizedTags is null
            || input.AuthorizedTags.Length is < 1 or > 100
            || input.AuthorizedTags.Any(t =>
                string.IsNullOrWhiteSpace(t) || t.Length > 20 || t.Any(char.IsControl)
            )
        )
            throw new RequestFailure(
                400,
                "invalid_device_details",
                "Enter the device identity, serial number and 1–100 authorized tags (up to 20 characters each)."
            );
        // Keep the auto-provisioned simulator namespace independent of hardware.
        if (input.Identity.StartsWith("SIM-", StringComparison.Ordinal))
            throw new RequestFailure(
                400,
                "reserved_device_identity",
                "SIM- is reserved for demo chargers."
            );
        await using var transaction = await StationMutationLock.BeginAsync(db, stationId, ct);
        var station = await access.StationAsync(actor, stationId, ct);
        if (station.IsArchived || station.IsDemo)
            throw new RequestFailure(
                409,
                "station_unavailable",
                "Choose an active physical station."
            );
        if (
            await db.OcppConnectors.AnyAsync(c => c.StationId == stationId, ct)
            || await db.OcppChargePoints.AnyAsync(d => d.Identity == input.Identity, ct)
        )
            throw new RequestFailure(
                409,
                "device_already_provisioned",
                "The station or device identity is already provisioned."
            );
        if (
            await db.ChargingSessions.AnyAsync(s => s.PointId == stationId && s.EndedAt == null, ct)
        )
            throw new RequestFailure(409, "station_busy", "Finish the active session first.");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var device = new OcppChargePoint
        {
            Identity = input.Identity,
            ProvisionedSerialNumber = input.SerialNumber!.Trim(),
            Vendor = input.Vendor?.Trim(),
            Model = input.Model?.Trim(),
            IsEnabled = true,
            ProtocolVersion = "1.6J",
            ManagementVersion = 1,
            CredentialVersion = 1,
            AuthorizedTagHashesJson = JsonSerializer.Serialize(
                input.AuthorizedTags.Select(DeviceTagAuthorization.Hash).Distinct()
            ),
        };
        device.PasswordHash = deviceHasher.HashPassword(device, password);
        db.OcppChargePoints.Add(device);
        for (var number = 1; number <= station.ConnectorCount; number++)
        {
            var connector = await db.ChargingConnectors.SingleOrDefaultAsync(
                c => c.StationId == stationId && c.Number == number,
                ct
            );
            if (connector is null)
            {
                connector = new ChargingConnector { StationId = stationId, Number = number };
                db.ChargingConnectors.Add(connector);
            }
            db.OcppConnectors.Add(
                new OcppConnector
                {
                    ChargePointId = device.Id,
                    ConnectorNumber = number,
                    StationId = stationId,
                    BusinessConnectorId = connector.Id,
                }
            );
        }
        // New hardware starts with unknown availability until fresh device reports.
        station.Status = station.ManualRestriction switch
        {
            ManualRestriction.Maintenance => PointState.Maintenance,
            ManualRestriction.OutOfService => PointState.OutOfService,
            _ => PointState.Unknown,
        };
        station.StatusSource = "Device";
        station.StatusReceivedAt = null;
        station.StatusObservedAt = null;
        station.Version++;
        access.Audit(
            actor,
            stationId,
            "DeviceProvisioned",
            null,
            new
            {
                device.Identity,
                SerialNumber = device.ProvisionedSerialNumber,
                Protocol = device.ProtocolVersion,
                Connectors = station.ConnectorCount,
                AuthorizedTagCount = input.AuthorizedTags.Distinct().Count(),
            }
        );
        ChargingRealtime.Stage(
            db,
            stationId,
            "StationChanged",
            "ChargingStation",
            station.Id,
            station.Version,
            clock.GetUtcNow(),
            station: ChargingRealtime.StationDto(station, clock.GetUtcNow())
        );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return new(
            await DeviceDetailsAsync(device, ct),
            device.Identity,
            password,
            "/ws/cs/" + device.Identity
        );
    }

    public async Task<DeviceCredentials?> ControlDeviceAsync(
        Guid actorId,
        Guid stationId,
        DeviceControlInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        await using var transaction = await StationMutationLock.BeginAsync(db, stationId, ct);
        var station = await access.StationAsync(actor, stationId, ct);
        var device =
            await MappedDeviceAsync(stationId, ct)
            ?? throw new RequestFailure(404, "device_not_found", "Device not found.");
        if (station.IsDemo || device.PasswordHash is null || station.IsArchived)
            throw new RequestFailure(
                409,
                "device_unavailable",
                "Choose an active physical station."
            );
        Version(device.ManagementVersion, input.Version);
        // An administrative disconnect must never interrupt an active customer.
        if (
            (!input.Enabled || input.RotatePassword)
            && await db.ChargingSessions.AnyAsync(
                s => s.PointId == stationId && s.EndedAt == null,
                ct
            )
        )
            throw new RequestFailure(
                409,
                "station_busy",
                "Finish the active session before disabling or rotating the device credentials."
            );
        var before = new { device.IsEnabled, Version = device.ManagementVersion };
        string? password = null;
        if (input.RotatePassword)
        {
            password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            device.PasswordHash = deviceHasher.HashPassword(device, password);
        }
        if (device.IsEnabled != input.Enabled || input.RotatePassword)
            device.CredentialVersion++;
        device.IsEnabled = input.Enabled;
        device.ManagementVersion++;
        if (!input.Enabled || input.RotatePassword)
        {
            foreach (
                var connection in await db
                    .OcppConnections.Where(c =>
                        c.ChargePointId == device.Id && c.DisconnectedAt == null
                    )
                    .ToListAsync(ct)
            )
            {
                connection.DisconnectedAt = clock.GetUtcNow();
                connection.CloseReason = "Administrative credential revocation";
            }
            device.LastSeenAt = null;
            foreach (
                var connector in await db
                    .ChargingConnectors.Where(c => c.StationId == stationId)
                    .ToListAsync(ct)
            )
            {
                connector.IsOnline = false;
                connector.Version++;
            }
            await projection.RefreshAsync(stationId, ct);
        }
        access.Audit(
            actor,
            stationId,
            input.RotatePassword ? "DeviceCredentialsRotated" : "DeviceAccessChanged",
            before,
            new { device.IsEnabled, Version = device.ManagementVersion }
        );
        ChargingRealtime.StageOperations(
            db,
            stationId,
            "DeviceChanged",
            "Device",
            device.Id,
            device.ManagementVersion,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return password is null
            ? null
            : new(
                await DeviceDetailsAsync(device, ct),
                device.Identity,
                password,
                "/ws/cs/" + device.Identity
            );
    }
}

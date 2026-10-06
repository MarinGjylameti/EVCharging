using System.Text.RegularExpressions;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService
{
    public async Task<DeviceDto> UpdateDeviceIdentityAsync(
        Guid actorId,
        Guid stationId,
        DeviceIdentityInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        await using var transaction = await StationMutationLock.BeginAsync(db, stationId, ct);
        var station = await access.StationAsync(actor, stationId, ct);
        var device =
            await MappedDeviceAsync(stationId, ct)
            ?? throw new RequestFailure(404, "device_not_found", "Device not found.");
        if (station.IsArchived || station.IsDemo || device.ProvisionedSerialNumber is null)
            throw new RequestFailure(
                409,
                "device_unavailable",
                "Choose an active physical station."
            );
        Version(device.ManagementVersion, input.Version);
        if (input.Identity is null || !Regex.IsMatch(input.Identity, "^[A-Za-z0-9_-]{2,40}\\z"))
            throw new RequestFailure(
                400,
                "invalid_device_identity",
                "Enter the charger's exact Charge Point ID (2–40 letters, digits, underscores or hyphens)."
            );
        if (input.Identity.StartsWith("SIM-", StringComparison.Ordinal))
            throw new RequestFailure(
                400,
                "reserved_device_identity",
                "SIM- is reserved for demo chargers."
            );
        if (input.Identity == device.Identity)
            return await DeviceDetailsAsync(device, ct);
        if (
            await db.ChargingSessions.AnyAsync(s => s.PointId == stationId && s.EndedAt == null, ct)
        )
            throw new RequestFailure(
                409,
                "station_busy",
                "Finish the active charge before changing the device identity."
            );
        if (
            await db.OcppChargePoints.AnyAsync(
                d => d.Identity == input.Identity && d.Id != device.Id,
                ct
            )
        )
            throw new RequestFailure(
                409,
                "device_already_provisioned",
                "This Charge Point ID is already registered."
            );

        var before = new { device.Identity };
        // Keep the same device key, connector mappings, Driver assignment and history.
        // Existing authenticated sockets keep their device key; future handshakes use the new ID.
        device.Identity = input.Identity;
        device.ManagementVersion++;
        access.Audit(actor, stationId, "DeviceIdentityChanged", before, new { device.Identity });
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
        return await DeviceDetailsAsync(device, ct);
    }
}

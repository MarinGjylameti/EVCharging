using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService
{
    public async Task<DeviceDto> AssignDeviceDriverAsync(
        Guid actorId,
        Guid stationId,
        DeviceDriverInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct, true);
        var tag = DeviceDriverAssignment.ParseCard(input.CardInput);
        if (tag is not null && input.DriverId is null)
            throw new RequestFailure(
                400,
                "driver_required",
                "Select the Driver who owns this card."
            );
        await using var transaction = await StationMutationLock.BeginAsync(db, stationId, ct);
        var station = await access.StationAsync(actor, stationId, ct);
        var device =
            await MappedDeviceAsync(stationId, ct)
            ?? throw new RequestFailure(
                404,
                "device_not_found",
                "Register the physical charger first."
            );
        if (station.IsArchived || station.IsDemo || device.ProvisionedSerialNumber is null)
            throw new RequestFailure(
                409,
                "device_unavailable",
                "Choose an active physical station."
            );
        Version(device.ManagementVersion, input.Version);
        if (
            input.DriverId is { } driverId
            && !await db.Accounts.AnyAsync(
                a =>
                    a.Id == driverId
                    && a.Role == UserRole.Driver
                    && a.IsActive
                    && a.ArchivedAt == null,
                ct
            )
        )
            throw new RequestFailure(400, "invalid_driver", "Choose an active Driver account.");
        if (device.AssignedDriverId == input.DriverId && device.DriverCardIdTag == tag)
            return await DeviceDetailsAsync(device, ct);
        if (
            await db.ChargingSessions.AnyAsync(s => s.PointId == stationId && s.EndedAt == null, ct)
        )
            throw new RequestFailure(
                409,
                "station_busy",
                "Finish the active charge before changing its recipient."
            );
        var previousDriver = device.AssignedDriverId;
        var before = new
        {
            device.AssignedDriverId,
            Mode = Mode(device.AssignedDriverId, device.DriverCardIdTag),
        };
        device.AssignedDriverId = input.DriverId;
        device.DriverCardIdTag = tag;
        device.DriverAssignmentSince = clock.GetUtcNow();
        device.ManagementVersion++;
        access.Audit(
            actor,
            stationId,
            "DeviceDriverAssigned",
            before,
            new { device.AssignedDriverId, Mode = Mode(input.DriverId, tag) }
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
        foreach (
            var recipient in new[] { previousDriver, input.DriverId }.OfType<Guid>().Distinct()
        )
            ChargingRealtime.Stage(
                db,
                stationId,
                "DeviceAccessChanged",
                "Device",
                device.Id,
                device.ManagementVersion,
                clock.GetUtcNow(),
                recipient
            );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return await DeviceDetailsAsync(device, ct);
    }

    private static string Mode(Guid? driverId, string? tag) =>
        driverId is null ? "Unassigned"
        : tag is null ? "Demo"
        : "Card";
}

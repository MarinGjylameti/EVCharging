using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationManagementService(
    ChargingDbContext db,
    TimeProvider clock,
    StationEventSignal events,
    StationProjection projection,
    OperationsAccess access,
    OperationsNotifications notifications
) : IStationManagement
{
    public async Task<IReadOnlyList<StationDto>> ListAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await access.ActorAsync(actorId, ct);
        var stations = await access
            .Stations(actor)
            .AsNoTracking()
            .OrderBy(station => station.City)
            .ThenBy(station => station.Name)
            .ToListAsync(ct);

        return stations.Select(StationDto.From).ToArray();
    }

    public async Task<StationDto> CreateAsync(
        StationInput input,
        Guid actorId,
        CancellationToken ct
    )
    {
        var data = StationRules.Validate(input);
        var actor = await access.ActorAsync(actorId, ct);
        await using var transaction = await LocationMutationLock.BeginAsync(
            db,
            input.LocationId,
            ct
        );
        await access.DestinationAsync(actor, input.LocationId, ct);
        if (await db.Stations.AnyAsync(station => station.Code == data.Code, ct))
            throw new RequestFailure(409, "station_code_taken", "The station code already exists.");

        var station = new ChargingStation();
        Apply(station, data);
        station.LocationId = input.LocationId;
        db.Stations.Add(station);
        access.Audit(
            actor,
            station.Id,
            "StationCreated",
            null,
            StationDto.From(station),
            input.Reason
        );
        AddPrice(actorId, station, null, input.Reason ?? "Station created");
        StageChange(station);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();

        return StationDto.From(station);
    }

    public async Task<StationDto> UpdateAsync(
        Guid id,
        StationInput input,
        Guid actorId,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct);
        await using var transaction = await StationMutationLock.BeginAsync(db, id, ct);
        var station = await access.StationAsync(actor, id, ct);
        if (station.IsArchived)
            throw new RequestFailure(
                409,
                "station_archived",
                "An archived station cannot be edited."
            );

        CheckVersion(station, input.Version);
        var data = StationRules.Validate(input);
        if (input.LocationId != station.LocationId)
            await access.DestinationAsync(actor, input.LocationId, ct);
        var before = StationDto.From(station);
        if (
            (data.PricePerKWh != station.PricePerKWh || data.Status != station.Status)
            && input.Reason?.Trim().Length is not (>= 3 and <= 500)
        )
            throw new RequestFailure(
                400,
                "change_reason_required",
                "Enter a reason for a status or price change (3–500 characters)."
            );
        if (input.Reason?.Length > 500)
            throw new RequestFailure(400, "invalid_reason", "Use up to 500 characters.");
        if (data.ConnectorCount != station.ConnectorCount)
            await ExpandPhysicalConnectorsAsync(station, data.ConnectorCount, ct);
        if (await db.Stations.AnyAsync(other => other.Code == data.Code && other.Id != id, ct))
            throw new RequestFailure(409, "station_code_taken", "The station code already exists.");

        Apply(station, data);
        station.LocationId = input.LocationId;
        station.Version++;
        if (before.PricePerKWh != station.PricePerKWh)
            AddPrice(actorId, station, before.PricePerKWh, input.Reason!.Trim());
        // Keep device availability and manual restrictions in the same projection.
        if (await db.ChargingConnectors.AnyAsync(x => x.StationId == station.Id, ct))
            await projection.RefreshAsync(station.Id, ct);
        else
            StageChange(station);
        access.Audit(
            actor,
            station.Id,
            "StationUpdated",
            before,
            StationDto.From(station),
            input.Reason?.Trim()
        );
        if (before.PricePerKWh != station.PricePerKWh)
            await notifications.EnqueueAsync(
                id,
                $"price-changed:{id}:{station.Version}",
                EVCharging.Domain.Notifications.NotificationKind.PriceChanged,
                ct
            );
        if (
            before.Status != station.Status.ToString()
            || before.ManualRestriction != station.ManualRestriction?.ToString()
        )
            await notifications.EnqueueAsync(
                id,
                $"manual-status:{id}:{station.Version}",
                EVCharging.Domain.Notifications.NotificationKind.ManualStatusChanged,
                ct
            );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();

        return StationDto.From(station);
    }

    public async Task<StationDto> ArchiveAsync(
        Guid id,
        ArchiveStationInput input,
        Guid actorId,
        CancellationToken ct
    )
    {
        await using var transaction = await StationMutationLock.BeginAsync(db, id, ct);
        var actor = await access.ActorAsync(actorId, ct);
        var station = await access.StationAsync(actor, id, ct);
        CheckVersion(station, input.Version);
        if (station.IsArchived)
            throw new RequestFailure(409, "station_archived", "Station is already archived.");

        if (
            station.Status is PointState.Charging or PointState.Occupied
            || await db.ChargingSessions.AnyAsync(x => x.PointId == id && x.EndedAt == null, ct)
            || await db.ChargingConnectionRequests.AnyAsync(
                x =>
                    x.StationId == id
                    && (
                        x.State == "Linked"
                        || x.State == "Waiting" && x.ExpiresAt > clock.GetUtcNow()
                    ),
                ct
            )
        )
            throw new RequestFailure(409, "station_busy", "A busy station cannot be archived.");

        var before = StationDto.From(station);
        station.IsArchived = true;
        station.Version++;
        access.Audit(actor, station.Id, "StationArchived", before, StationDto.From(station));
        StageChange(station);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();

        return StationDto.From(station);
    }

    private static void CheckVersion(ChargingStation station, long version)
    {
        if (station.Version != version)
            throw new RequestFailure(
                412,
                "version_conflict",
                "The station changed. Refresh and try again."
            );
    }

    private async Task ExpandPhysicalConnectorsAsync(
        ChargingStation station,
        int count,
        CancellationToken ct
    )
    {
        var mappings = await db
            .OcppConnectors.Where(x => x.StationId == station.Id)
            .ToListAsync(ct);
        if (mappings.Count == 0)
            return;
        var deviceIds = mappings.Select(x => x.ChargePointId).Distinct().ToArray();
        if (station.IsDemo || count < station.ConnectorCount || deviceIds.Length != 1)
            throw new RequestFailure(
                409,
                "device_connectors_locked",
                "Only adding connectors to a physical device is supported; existing connectors and history cannot be removed."
            );
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == deviceIds[0], ct);
        if (device.ProvisionedSerialNumber is null)
            throw new RequestFailure(
                409,
                "device_connectors_locked",
                "Choose a provisioned physical device."
            );
        if (
            await db.ChargingSessions.AnyAsync(
                x => x.PointId == station.Id && x.EndedAt == null,
                ct
            )
        )
            throw new RequestFailure(
                409,
                "station_busy",
                "Finish active charging before adding connectors."
            );
        for (var number = station.ConnectorCount + 1; number <= count; number++)
        {
            var connector = new ChargingConnector { StationId = station.Id, Number = number };
            db.ChargingConnectors.Add(connector);
            db.OcppConnectors.Add(
                new OcppConnector
                {
                    ChargePointId = device.Id,
                    ConnectorNumber = number,
                    StationId = station.Id,
                    BusinessConnectorId = connector.Id,
                }
            );
        }
        device.ManagementVersion++;
        ChargingRealtime.StageOperations(
            db,
            station.Id,
            "DeviceChanged",
            "Device",
            device.Id,
            device.ManagementVersion,
            clock.GetUtcNow()
        );
    }

    private void Apply(ChargingStation station, ValidatedStation data)
    {
        station.Code = data.Code;
        station.Name = data.Name;
        station.City = data.City;
        station.Address = data.Address;
        station.Latitude = data.Latitude;
        station.Longitude = data.Longitude;
        station.ConnectorType = data.ConnectorType;
        station.ConnectorCount = data.ConnectorCount;
        station.PowerKW = data.PowerKW;
        station.PricePerKWh = data.PricePerKWh;
        station.Status = data.Status;
        station.ManualRestriction = data.Status switch
        {
            PointState.OutOfService => ManualRestriction.OutOfService,
            PointState.Maintenance => ManualRestriction.Maintenance,
            _ => null,
        };
        station.StatusSource = "Manual";
        station.StatusObservedAt = clock.GetUtcNow();
        station.StatusReceivedAt = station.StatusObservedAt;
        AvailabilityForecastRules.Clear(station);
    }

    private void StageChange(ChargingStation station) =>
        ChargingRealtime.Stage(
            db,
            station.Id,
            station.IsArchived ? "StationRemoved" : "StationChanged",
            "ChargingStation",
            station.Id,
            station.Version,
            clock.GetUtcNow(),
            station: station.IsArchived
                ? null
                : ChargingRealtime.StationDto(station, clock.GetUtcNow())
        );

    private void AddPrice(Guid actorId, ChargingStation station, decimal? before, string reason) =>
        db.PriceRevisions.Add(
            new PriceRevision
            {
                StationId = station.Id,
                ActorId = actorId,
                PreviousPrice = before,
                PricePerKWh = station.PricePerKWh,
                Currency = station.Currency,
                Reason = reason,
                RecordedAt = clock.GetUtcNow(),
            }
        );
}

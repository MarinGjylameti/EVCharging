using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed class StationManagementService(
    ChargingDbContext db,
    TimeProvider clock,
    StationEventSignal events,
    StationProjection projection
) : IStationManagement
{
    public async Task<IReadOnlyList<StationDto>> ListAsync(CancellationToken ct)
    {
        var stations = await db
            .Stations.AsNoTracking()
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
        if (await db.Stations.AnyAsync(station => station.Code == data.Code, ct))
            throw new RequestFailure(409, "station_code_taken", "The station code already exists.");

        var station = new ChargingStation();
        Apply(station, data);
        db.Stations.Add(station);
        AddAudit(actorId, station.Id, "StationCreated");
        StageChange(station);
        await db.SaveChangesAsync(ct);
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
        var station = await GetAsync(id, ct);
        if (station.IsArchived)
            throw new RequestFailure(
                409,
                "station_archived",
                "An archived station cannot be edited."
            );

        CheckVersion(station, input.Version);
        var data = StationRules.Validate(input);
        if (await db.Stations.AnyAsync(other => other.Code == data.Code && other.Id != id, ct))
            throw new RequestFailure(409, "station_code_taken", "The station code already exists.");

        Apply(station, data);
        station.Version++;
        AddAudit(actorId, station.Id, "StationUpdated");
        // Keep device availability and manual restrictions in the same projection.
        if (await db.ChargingConnectors.AnyAsync(x => x.StationId == station.Id, ct))
            await projection.RefreshAsync(station.Id, ct);
        else
            StageChange(station);
        await db.SaveChangesAsync(ct);
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
        var station = await GetAsync(id, ct);
        CheckVersion(station, input.Version);
        if (station.IsArchived)
            throw new RequestFailure(409, "station_archived", "Station is already archived.");

        if (station.Status is PointState.Charging or PointState.Occupied)
            throw new RequestFailure(409, "station_busy", "A busy station cannot be archived.");

        station.IsArchived = true;
        station.Version++;
        AddAudit(actorId, station.Id, "StationArchived");
        StageChange(station);
        await db.SaveChangesAsync(ct);
        events.Notify();

        return StationDto.From(station);
    }

    private async Task<ChargingStation> GetAsync(Guid id, CancellationToken ct) =>
        await db.Stations.SingleOrDefaultAsync(station => station.Id == id, ct)
        ?? throw new RequestFailure(404, "station_not_found", "Station not found.");

    private static void CheckVersion(ChargingStation station, long version)
    {
        if (station.Version != version)
            throw new RequestFailure(
                412,
                "version_conflict",
                "The station changed. Refresh and try again."
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

    private void AddAudit(Guid actorId, Guid stationId, string action) =>
        db.Audit.Add(
            new AuditEntry
            {
                ActorId = actorId,
                ActorRole = "Superadmin",
                EntityId = stationId,
                Action = action,
                RecordedAt = clock.GetUtcNow(),
            }
        );
}

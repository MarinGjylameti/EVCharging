using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Api.Endpoints;

public sealed record StationInput(string? Code, string? Name, string? City, string? Address,
    double Latitude, double Longitude, string? ConnectorType, int ConnectorCount,
    decimal PowerKW, decimal PricePerKWh, string? Status, long Version = 0);
public sealed record ArchiveStationInput(long Version);

public static class StationManagementEndpoints
{
    public static void MapStationManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/stations").RequireAuthorization("Operator");
        group.MapGet("", async (ChargingDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Stations.AsNoTracking().OrderBy(x => x.City).ThenBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(rows.Select(StationDto));
        });

        group.MapPost("", async (StationInput input, ClaimsPrincipal user, ChargingDbContext db, CancellationToken ct) =>
        {
            var data = Validate(input);
            if (await db.Stations.AnyAsync(x => x.Code == data.Code, ct))
                throw new RequestFailure(409, "station_code_taken", "The station code already exists.");
            var station = new ChargingStation();
            Apply(station, data);
            db.Stations.Add(station);
            Audit(db, user, station.Id, "StationCreated");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/stations/{station.Id}", StationDto(station));
        }).RequireAuthorization("Superadmin");

        group.MapPatch("/{id:guid}", async (Guid id, StationInput input, ClaimsPrincipal user, ChargingDbContext db, CancellationToken ct) =>
        {
            var station = await db.Stations.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new RequestFailure(404, "station_not_found", "Station not found.");
            if (station.IsArchived) throw new RequestFailure(409, "station_archived", "An archived station cannot be edited.");
            if (station.Version != input.Version)
                throw new RequestFailure(412, "version_conflict", "The station changed. Refresh and try again.");
            var data = Validate(input);
            if (await db.Stations.AnyAsync(x => x.Code == data.Code && x.Id != id, ct))
                throw new RequestFailure(409, "station_code_taken", "The station code already exists.");
            Apply(station, data);
            station.Version++;
            Audit(db, user, station.Id, "StationUpdated");
            await db.SaveChangesAsync(ct);
            return Results.Ok(StationDto(station));
        }).RequireAuthorization("Superadmin");

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveStationInput input, ClaimsPrincipal user, ChargingDbContext db, CancellationToken ct) =>
        {
            var station = await db.Stations.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new RequestFailure(404, "station_not_found", "Station not found.");
            if (station.Version != input.Version)
                throw new RequestFailure(412, "version_conflict", "The station changed. Refresh and try again.");
            if (station.IsArchived) throw new RequestFailure(409, "station_archived", "Station is already archived.");
            if (station.Status is PointState.Charging or PointState.Occupied)
                throw new RequestFailure(409, "station_busy", "A busy station cannot be archived.");
            station.IsArchived = true;
            station.Version++;
            Audit(db, user, station.Id, "StationArchived");
            await db.SaveChangesAsync(ct);
            return Results.Ok(StationDto(station));
        }).RequireAuthorization("Superadmin");
    }

    private static object StationDto(ChargingStation x) => new
    {
        x.Id, x.Code, x.Name, x.City, x.Address, x.Latitude, x.Longitude,
        x.ConnectorType, x.ConnectorCount, x.PowerKW, x.PricePerKWh, x.Currency,
        status = x.Status.ToString(), x.IsArchived, x.IsDemo, x.Version
    };

    private static (string Code, string Name, string City, string Address, double Latitude,
        double Longitude, string ConnectorType, int ConnectorCount, decimal PowerKW,
        decimal PricePerKWh, PointState Status) Validate(StationInput input)
    {
        var code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var name = input.Name?.Trim() ?? "";
        var city = input.City?.Trim() ?? "";
        var address = input.Address?.Trim() ?? "";
        var connector = input.ConnectorType?.Trim() ?? "";
        if (code.Length is < 2 or > 40 || !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new RequestFailure(400, "invalid_station_code", "Use 2–40 uppercase letters, digits or hyphens.");
        if (name.Length is < 2 or > 120 || city.Length is < 2 or > 80 || address.Length is < 2 or > 200 || connector.Length is < 2 or > 40)
            throw new RequestFailure(400, "invalid_station_details", "Station name, city, address or connector is invalid.");
        if (!double.IsFinite(input.Latitude) || !double.IsFinite(input.Longitude) || input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180)
            throw new RequestFailure(400, "invalid_coordinates", "Coordinates are invalid.");
        if (input.ConnectorCount is < 1 or > 100 || input.PowerKW is <= 0 or > 99999999m || input.PricePerKWh is < 0 or > 999999m)
            throw new RequestFailure(400, "invalid_station_capacity", "Connector count, power or price is invalid.");
        if (!Enum.TryParse<PointState>(input.Status, false, out var status) || !Enum.IsDefined(status))
            throw new RequestFailure(400, "invalid_station_status", "Station status is invalid.");
        return (code, name, city, address, input.Latitude, input.Longitude, connector,
            input.ConnectorCount, input.PowerKW, input.PricePerKWh, status);
    }

    private static void Apply(ChargingStation station, (string Code, string Name, string City, string Address,
        double Latitude, double Longitude, string ConnectorType, int ConnectorCount, decimal PowerKW,
        decimal PricePerKWh, PointState Status) data)
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
        // A manual station edit may also represent a new charging session.
        AvailabilityForecastRules.Clear(station);
    }

    private static void Audit(ChargingDbContext db, ClaimsPrincipal user, Guid id, string action) =>
        db.Audit.Add(new AuditEntry { ActorId = user.UserId(), ActorRole = "Superadmin", EntityId = id, Action = action });
}

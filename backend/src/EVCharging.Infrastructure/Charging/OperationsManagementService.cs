using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Application.Identity;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

public sealed partial class OperationsManagementService(
    ChargingDbContext db,
    OperationsAccess access,
    IPasswordHasher<Account> accountsHasher,
    IPasswordHasher<OcppChargePoint> deviceHasher,
    TimeProvider clock,
    StationEventSignal signal,
    StationProjection projection
) : IOperationsManagement
{
    private static LocationDto Location(ChargingLocation x) =>
        new(x.Id, x.Name, x.City, x.Address, x.Latitude, x.Longitude, x.IsArchived, x.Version);

    private static void Version(long actual, long expected)
    {
        if (actual != expected)
            throw new RequestFailure(
                412,
                "version_conflict",
                "Refresh the resource and try again."
            );
    }

    public async Task<IReadOnlyList<LocationDto>> LocationsAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await access.ActorAsync(actorId, ct);
        return (await access.Locations(actor).AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct))
            .Select(Location)
            .ToArray();
    }

    public async Task<LocationDto> SaveLocationAsync(
        Guid actorId,
        Guid? id,
        LocationInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct, true);
        await using var transaction = await LocationMutationLock.BeginAsync(db, id, ct);
        if (
            input.Name?.Trim().Length is not (>= 2 and <= 120)
            || input.City?.Trim().Length is not (>= 2 and <= 80)
            || input.Address?.Trim().Length is not (>= 2 and <= 200)
            || !double.IsFinite(input.Latitude)
            || input.Latitude is < -90 or > 90
            || !double.IsFinite(input.Longitude)
            || input.Longitude is < -180 or > 180
        )
            throw new RequestFailure(
                400,
                "invalid_location",
                "Enter valid location details and coordinates."
            );
        var location = id is null
            ? new ChargingLocation()
            : await db.Locations.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new RequestFailure(404, "location_not_found", "Location not found.");
        if (id is not null)
            Version(location.Version, input.Version);
        if (location.IsArchived)
            throw new RequestFailure(409, "location_archived", "This location is archived.");
        var before = id is null ? null : Location(location);
        location.Name = input.Name!.Trim();
        location.City = input.City!.Trim();
        location.Address = input.Address!.Trim();
        location.Latitude = input.Latitude;
        location.Longitude = input.Longitude;
        if (id is null)
            db.Locations.Add(location);
        else
            location.Version++;
        access.Audit(
            actor,
            location.Id,
            id is null ? "LocationCreated" : "LocationUpdated",
            before,
            Location(location)
        );
        ChargingRealtime.StageOperations(
            db,
            Guid.Empty,
            "LocationChanged",
            "Location",
            location.Id,
            location.Version,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return Location(location);
    }

    public async Task<LocationDto> ArchiveLocationAsync(
        Guid actorId,
        Guid id,
        long version,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct, true);
        await using var transaction = await LocationMutationLock.BeginAsync(db, id, ct);
        var location =
            await db.Locations.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new RequestFailure(404, "location_not_found", "Location not found.");
        Version(location.Version, version);
        if (location.IsArchived)
            throw new RequestFailure(
                409,
                "location_archived",
                "This location is already archived."
            );
        if (await db.Stations.AnyAsync(x => x.LocationId == id && !x.IsArchived, ct))
            throw new RequestFailure(
                409,
                "location_has_stations",
                "Move or archive this location's active stations first."
            );
        var before = Location(location);
        location.IsArchived = true;
        location.Version++;
        access.Audit(actor, id, "LocationArchived", before, Location(location));
        ChargingRealtime.StageOperations(
            db,
            Guid.Empty,
            "LocationChanged",
            "Location",
            id,
            location.Version,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return Location(location);
    }

    private async Task<AdministratorDetails> AdministratorAsync(
        Account account,
        CancellationToken ct
    )
    {
        var grants = await db
            .AdministratorGrants.AsNoTracking()
            .Where(x => x.AdministratorId == account.Id)
            .ToListAsync(ct);
        return new(
            account.Id,
            account.Email,
            account.FirstName,
            account.LastName,
            account.Phone,
            account.IsActive,
            account.ArchivedAt,
            account.Version,
            grants.Where(x => x.LocationId != null).Select(x => x.LocationId!.Value).ToArray(),
            grants.Where(x => x.StationId != null).Select(x => x.StationId!.Value).ToArray()
        );
    }

    public async Task<IReadOnlyList<AdministratorDetails>> AdministratorsAsync(
        Guid actorId,
        CancellationToken ct
    )
    {
        await access.ActorAsync(actorId, ct, true);
        var accounts = await db
            .Accounts.AsNoTracking()
            .Where(x => x.Role == UserRole.Admin)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToListAsync(ct);
        var grants = await db.AdministratorGrants.AsNoTracking().ToListAsync(ct);
        return accounts
            .Select(a => new AdministratorDetails(
                a.Id,
                a.Email,
                a.FirstName,
                a.LastName,
                a.Phone,
                a.IsActive,
                a.ArchivedAt,
                a.Version,
                grants
                    .Where(g => g.AdministratorId == a.Id && g.LocationId != null)
                    .Select(g => g.LocationId!.Value)
                    .ToArray(),
                grants
                    .Where(g => g.AdministratorId == a.Id && g.StationId != null)
                    .Select(g => g.StationId!.Value)
                    .ToArray()
            ))
            .ToArray();
    }

    public async Task<AdministratorDetails> SaveAdministratorAsync(
        Guid actorId,
        Guid? id,
        AdministratorInput input,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct, true);
        var email = AccountRules.NormalizeEmail(input.Email);
        var first = AccountRules.Name(input.FirstName);
        var last = AccountRules.Name(input.LastName);
        var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : AccountRules.Phone(input.Phone);
        if (
            input.LocationIds is null
            || input.StationIds is null
            || input.LocationIds.Length + input.StationIds.Length > 100
        )
            throw new RequestFailure(400, "invalid_assignments", "Use up to 100 assignments.");
        var locationIds = input.LocationIds.Distinct().ToArray();
        var stationIds = input.StationIds.Distinct().ToArray();
        if (
            await db.Locations.CountAsync(
                x =>
                    locationIds.Contains(x.Id)
                    && (
                        !x.IsArchived
                        || id != null
                            && db.AdministratorGrants.Any(g =>
                                g.AdministratorId == id && g.LocationId == x.Id
                            )
                    ),
                ct
            ) != locationIds.Length
            || await db.Stations.CountAsync(
                x =>
                    stationIds.Contains(x.Id)
                    && (
                        !x.IsArchived
                        || id != null
                            && db.AdministratorGrants.Any(g =>
                                g.AdministratorId == id && g.StationId == x.Id
                            )
                    ),
                ct
            ) != stationIds.Length
        )
            throw new RequestFailure(
                400,
                "invalid_assignments",
                "Assign only existing active resources."
            );
        if (await db.Accounts.AnyAsync(x => x.NormalizedEmail == email && x.Id != id, ct))
            throw new RequestFailure(409, "email_unavailable", "This email is already in use.");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        if (id is not null && db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM accounts WHERE id = {id.Value} FOR UPDATE",
                ct
            );
        var account = id is null
            ? new Account { Role = UserRole.Admin, CreatedAt = clock.GetUtcNow() }
            : await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == id && x.Role == UserRole.Admin,
                ct
            )
                ?? throw new RequestFailure(
                    404,
                    "administrator_not_found",
                    "Administrator not found."
                );
        if (id is not null)
            Version(account.Version, input.Version);
        if (account.ArchivedAt is not null)
            throw new RequestFailure(
                409,
                "administrator_archived",
                "Archived administrators cannot be edited."
            );
        var before = id is null ? null : await AdministratorAsync(account, ct);
        account.Email = input.Email.Trim();
        account.NormalizedEmail = email;
        account.FirstName = first;
        account.LastName = last;
        account.Phone = phone;
        account.IsActive = input.IsActive;
        var passwordChanged = !string.IsNullOrEmpty(input.Password);
        if (id is null || passwordChanged)
        {
            AccountRules.ValidatePassword(input.Password);
            account.PasswordHash = accountsHasher.HashPassword(account, input.Password!);
        }
        if (id is null)
            db.Accounts.Add(account);
        else
            account.Version++;
        var old = await db
            .AdministratorGrants.Where(x => x.AdministratorId == account.Id)
            .ToListAsync(ct);
        // Keep retained grants, avoiding delete+insert against the same unique key.
        db.AdministratorGrants.RemoveRange(
            old.Where(x =>
                x.LocationId is { } l
                    ? !locationIds.Contains(l)
                    : !stationIds.Contains(x.StationId!.Value)
            )
        );
        foreach (var l in locationIds.Where(l => !old.Any(g => g.LocationId == l)))
            db.AdministratorGrants.Add(
                new AdministratorGrant { AdministratorId = account.Id, LocationId = l }
            );
        foreach (var s in stationIds.Where(s => !old.Any(g => g.StationId == s)))
            db.AdministratorGrants.Add(
                new AdministratorGrant { AdministratorId = account.Id, StationId = s }
            );
        if (!account.IsActive || passwordChanged)
            await RevokeAsync(account.Id, ct);
        var after = new AdministratorDetails(
            account.Id,
            account.Email,
            account.FirstName,
            account.LastName,
            account.Phone,
            account.IsActive,
            null,
            account.Version,
            locationIds,
            stationIds
        );
        access.Audit(
            actor,
            account.Id,
            id is null ? "AdministratorCreated" : "AdministratorUpdated",
            before,
            after
        );
        ChargingRealtime.StageOperations(
            db,
            Guid.Empty,
            "AdministratorAccessChanged",
            "Administrator",
            account.Id,
            account.Version,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        signal.Notify();
        return after;
    }

    private async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        foreach (
            var session in await db
                .Sessions.Where(x => x.UserId == id && x.RevokedAt == null)
                .ToListAsync(ct)
        )
            session.RevokedAt = clock.GetUtcNow();
        foreach (
            var device in await db.Devices.Where(x => x.UserId == id && x.IsActive).ToListAsync(ct)
        )
            device.IsActive = false;
    }

    public async Task<AdministratorDetails> ArchiveAdministratorAsync(
        Guid actorId,
        Guid id,
        long version,
        CancellationToken ct
    )
    {
        var actor = await access.ActorAsync(actorId, ct, true);
        var account =
            await db.Accounts.SingleOrDefaultAsync(x => x.Id == id && x.Role == UserRole.Admin, ct)
            ?? throw new RequestFailure(404, "administrator_not_found", "Administrator not found.");
        Version(account.Version, version);
        if (account.ArchivedAt is not null)
            throw new RequestFailure(
                409,
                "administrator_archived",
                "Administrator already archived."
            );
        var before = await AdministratorAsync(account, ct);
        account.IsActive = false;
        account.ArchivedAt = clock.GetUtcNow();
        account.Version++;
        await RevokeAsync(id, ct);
        var after = await AdministratorAsync(account, ct);
        access.Audit(actor, id, "AdministratorArchived", before, after);
        ChargingRealtime.StageOperations(
            db,
            Guid.Empty,
            "AdministratorAccessChanged",
            "Administrator",
            id,
            account.Version,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return after;
    }

    public async Task<IReadOnlyList<DriverDirectoryEntry>> DriversAsync(
        Guid actorId,
        CancellationToken ct
    )
    {
        await access.ActorAsync(actorId, ct, true);
        return await db
            .Accounts.AsNoTracking()
            .Where(x => x.Role == UserRole.Driver)
            .OrderBy(x => x.LastName)
            .Select(x => new DriverDirectoryEntry(
                x.Id,
                x.FirstName,
                x.LastName,
                x.Email,
                x.Phone,
                x.IsActive,
                x.CreatedAt
            ))
            .ToListAsync(ct);
    }
}

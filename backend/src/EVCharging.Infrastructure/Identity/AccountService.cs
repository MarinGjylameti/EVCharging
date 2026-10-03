using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Common;
using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Identity;

public sealed class SessionOptions
{
    public int LifetimeHours { get; set; } = 24;
}

public sealed class AccountService(
    ChargingDbContext db,
    IPasswordHasher<Account> hasher,
    IOptions<SessionOptions> options,
    TimeProvider clock,
    StationEventSignal events,
    StationProjection projection
)
{
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = AccountRules.NormalizeEmail(request.Email);
        AccountRules.ValidatePassword(request.Password);
        var user = new Account
        {
            Email = request.Email.Trim(),
            NormalizedEmail = email,
            FirstName = AccountRules.Name(request.FirstName),
            LastName = AccountRules.Name(request.LastName),
            Language = AccountRules.Language(request.Language),
            Role = UserRole.Driver,
            CreatedAt = clock.GetUtcNow(),
        };
        if (await db.Accounts.AnyAsync(x => x.NormalizedEmail == email, ct))
            throw new RequestFailure(409, "email_unavailable", "This email cannot be registered.");
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Accounts.Add(user);
        AddAudit(user, "DriverRegistered");
        var result = IssueSession(user);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = AccountRules.NormalizeEmail(request.Email);
        if (request.Password is null || request.Password.Length > 128)
            throw new RequestFailure(401, "invalid_credentials", "Email or password is incorrect.");
        var user = await db.Accounts.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
        if (user is null || !user.IsActive || user.ArchivedAt is not null)
        {
            // Do equivalent password work for unknown and disabled accounts.
            hasher.VerifyHashedPassword(new Account(), DummyPasswordHash.Value, request.Password);
            throw new RequestFailure(401, "invalid_credentials", "Email or password is incorrect.");
        }
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new RequestFailure(401, "invalid_credentials", "Email or password is incorrect.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        // Serialize session issuance with password changes/revocation via optimistic concurrency.
        user.Version++;
        var auth = IssueSession(user);
        await db.SaveChangesAsync(ct);
        return auth;
    }

    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<Account>().HashPassword(
            new Account(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        )
    );

    private AuthResult IssueSession(Account user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = clock.GetUtcNow().AddHours(options.Value.LifetimeHours);
        db.Sessions.Add(
            new LoginSession
            {
                UserId = user.Id,
                User = user,
                TokenHash = HashToken(token),
                ExpiresAt = expiry,
                CreatedAt = clock.GetUtcNow(),
            }
        );
        return new(token, expiry, AccountRules.ToDto(user));
    }

    public async Task<Account> GetAsync(Guid userId, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == userId && x.IsActive && x.ArchivedAt == null,
            ct
        ) ?? throw new RequestFailure(401, "session_expired", "Please sign in again.");

    public async Task<IReadOnlyList<AdministratorDto>> ListAdministratorsAsync(
        CancellationToken ct
    ) =>
        await db
            .Accounts.AsNoTracking()
            .Where(account => account.Role == UserRole.Admin)
            .OrderBy(account => account.CreatedAt)
            .Take(100)
            .Select(account => new AdministratorDto(
                account.Id,
                account.Email,
                account.FirstName,
                account.LastName,
                account.IsActive
            ))
            .ToListAsync(ct);

    public async Task<UserDto> UpdateProfileAsync(
        Guid userId,
        ProfileRequest request,
        CancellationToken ct
    )
    {
        var capacity = AccountRules.BatteryCapacity(request.BatteryCapacityKWh);
        var claim = await db
            .ChargingConnectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.DriverId == userId && (x.State == "Linked" || x.State == "Waiting"),
                ct
            );
        var connector = claim is null
            ? null
            : await db.ChargingConnectors.SingleAsync(
                x => x.StationId == claim.StationId && x.Number == claim.ConnectorNumber,
                ct
            );
        await using var transaction =
            connector is not null
                ? await ConnectorMutationLock.BeginAsync(db, connector.Id, userId, ct)
            : db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct)
            : null;
        if (connector is null && db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM accounts WHERE id = {userId} FOR UPDATE",
                ct
            );
        var user = await GetAsync(userId, ct);
        CheckVersion(user, request.Version);
        var lockedClaim = await db
            .ChargingConnectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.DriverId == userId && (x.State == "Linked" || x.State == "Waiting"),
                ct
            );
        if (
            lockedClaim is not null
            && (
                lockedClaim.StationId != claim?.StationId
                || lockedClaim.ConnectorNumber != claim?.ConnectorNumber
            )
        )
            throw new RequestFailure(
                412,
                "version_conflict",
                "Your charging association changed. Refresh and try again."
            );
        var before = new
        {
            user.FirstName,
            user.LastName,
            user.Phone,
            user.VehicleMake,
            user.VehicleModel,
            user.BatteryType,
            user.BatteryCapacityKWh,
        };
        user.FirstName = AccountRules.Name(request.FirstName);
        user.LastName = AccountRules.Name(request.LastName);
        user.Phone = Optional(request.Phone, 32);
        user.VehicleMake = Optional(request.VehicleMake, 80);
        user.VehicleModel = Optional(request.VehicleModel, 80);
        user.BatteryType = Optional(request.BatteryType, 80);
        user.BatteryCapacityKWh = capacity;
        user.Version++;
        AddAudit(
            user,
            "ProfileChanged",
            JsonSerializer.Serialize(
                new
                {
                    before,
                    after = new
                    {
                        user.FirstName,
                        user.LastName,
                        user.Phone,
                        user.VehicleMake,
                        user.VehicleModel,
                        user.BatteryType,
                        user.BatteryCapacityKWh,
                    },
                }
            )
        );
        if (before.BatteryCapacityKWh != capacity && connector is not null)
        {
            var active = await db.ChargingSessions.SingleOrDefaultAsync(
                x =>
                    x.ConnectorId == connector.Id
                    && x.EndedAt == null
                    && db.ChargingConnectionRequests.Any(c =>
                        c.DriverId == userId && c.State == "Linked" && c.SessionId == x.Id
                    ),
                ct
            );
            if (active is not null)
            {
                active.BatteryCapacityKWh = capacity;
                active.Version++;
                var station = await db.Stations.SingleAsync(x => x.Id == active.PointId, ct);
                var now = clock.GetUtcNow();
                ChargingRealtime.Stage(
                    db,
                    station.Id,
                    "SessionVehicleChanged",
                    "ChargingSession",
                    active.Id,
                    active.Version,
                    now,
                    userId,
                    session: ChargingRealtime.SessionDto(active, connector, station, now)
                );
                await projection.RefreshAsync(station.Id, ct);
            }
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();
        return AccountRules.ToDto(user);
    }

    public async Task<UserDto> UpdateSettingsAsync(
        Guid userId,
        SettingsRequest request,
        CancellationToken ct
    )
    {
        var user = await GetAsync(userId, ct);
        CheckVersion(user, request.Version);
        var before = new { user.Language, user.PushEnabled };
        user.Language = AccountRules.Language(request.Language);
        user.PushEnabled = request.PushEnabled;
        user.Version++;
        AddAudit(
            user,
            "SettingsChanged",
            JsonSerializer.Serialize(
                new { before, after = new { user.Language, user.PushEnabled } }
            )
        );
        await db.SaveChangesAsync(ct);
        return AccountRules.ToDto(user);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        PasswordRequest request,
        CancellationToken ct
    )
    {
        var user = await GetAsync(userId, ct);
        AccountRules.ValidatePassword(request.NewPassword);
        if (
            request.CurrentPassword is null
            || request.CurrentPassword.Length > 128
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword)
                == PasswordVerificationResult.Failed
        )
            throw new RequestFailure(400, "incorrect_password", "Current password is incorrect.");
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.Version++;
        var sessions = await db
            .Sessions.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var session in sessions)
            session.RevokedAt = clock.GetUtcNow();
        var devices = await db.Devices.Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var device in devices)
            device.IsActive = false;
        AddAudit(user, "PasswordChanged");
        await db.SaveChangesAsync(ct);
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(
            x => x.Id == sessionId && x.UserId == userId,
            ct
        );
        if (session is not null)
            session.RevokedAt = clock.GetUtcNow();
        foreach (
            var device in await db
                .Devices.Where(x => x.UserId == userId && x.SessionId == sessionId)
                .ToListAsync(ct)
        )
            device.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private void AddAudit(Account user, string action, string? changes = null) =>
        db.Audit.Add(
            new AuditEntry
            {
                ActorId = user.Id,
                ActorRole = user.Role.ToString(),
                Action = action,
                EntityId = user.Id,
                ChangesJson = changes,
                RecordedAt = clock.GetUtcNow(),
            }
        );

    private static void CheckVersion(Account user, long version)
    {
        if (user.Version != version)
            throw new RequestFailure(
                412,
                "version_conflict",
                "Your profile changed. Refresh and try again."
            );
    }

    private static string? Optional(string? value, int max)
    {
        value = value?.Trim();
        if (value?.Length > max)
            throw new RequestFailure(400, "field_too_long", "A field exceeds the allowed length.");
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

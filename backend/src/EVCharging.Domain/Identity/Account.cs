namespace EVCharging.Domain.Identity;

public enum UserRole
{
    Driver,
    Admin,
    Superadmin,
}

public enum AppLanguage
{
    Sq,
    En,
}

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? VehicleMake { get; set; }
    public string? VehicleModel { get; set; }
    public string? BatteryType { get; set; }
    public decimal? BatteryCapacityKWh { get; set; }
    public UserRole Role { get; set; } = UserRole.Driver;
    public AppLanguage Language { get; set; } = AppLanguage.Sq;
    public bool PushEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ArchivedAt { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Account User { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

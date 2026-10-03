using System.Net.Mail;
using EVCharging.Application.Common;
using EVCharging.Domain.Identity;

namespace EVCharging.Application.Identity;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string Language
);

public sealed record LoginRequest(string Email, string Password);

public sealed record PasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ProfileRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? VehicleMake,
    string? VehicleModel,
    string? BatteryType,
    long Version,
    decimal? BatteryCapacityKWh = null
);

public sealed record SettingsRequest(string Language, bool PushEnabled, long Version);

public sealed record DeviceRequest(string Token, string Platform);

public sealed record UserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string Language,
    bool PushEnabled,
    string? Phone,
    string? VehicleMake,
    string? VehicleModel,
    string? BatteryType,
    long Version,
    decimal? BatteryCapacityKWh = null
);

public sealed record AuthResult(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record AdministratorDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive
);

public static class AccountRules
{
    public static string NormalizeEmail(string? email)
    {
        var value = email?.Trim() ?? "";
        if (
            value.Length > 254
            || !MailAddress.TryCreate(value, out var parsed)
            || parsed.Address != value
        )
            throw new RequestFailure(400, "invalid_email", "Enter a valid email address.");
        return value.ToUpperInvariant();
    }

    public static void ValidatePassword(string? value)
    {
        if (value is null || value.Length is < 12 or > 128)
            throw new RequestFailure(
                400,
                "invalid_password",
                "Use a password between 12 and 128 characters."
            );
    }

    public static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 80)
            throw new RequestFailure(400, "invalid_name", "Names must contain 1 to 80 characters.");
        return name;
    }

    public static AppLanguage Language(string? language) =>
        language switch
        {
            "sq" => AppLanguage.Sq,
            "en" => AppLanguage.En,
            _ => throw new RequestFailure(400, "invalid_language", "Choose sq or en."),
        };

    public static decimal? BatteryCapacity(decimal? value)
    {
        if (
            value is not null
            && (value is <= 0 or > 1000 || decimal.Round(value.Value, 2) != value)
        )
            throw new RequestFailure(
                400,
                "invalid_battery_capacity",
                "Enter a usable battery capacity between 0.01 and 1000 kWh, with at most two decimal places."
            );
        return value;
    }

    public static UserDto ToDto(Account user) =>
        new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString().ToUpperInvariant(),
            user.Language == AppLanguage.Sq ? "sq" : "en",
            user.PushEnabled,
            user.Phone,
            user.VehicleMake,
            user.VehicleModel,
            user.BatteryType,
            user.Version,
            user.BatteryCapacityKWh
        );
}

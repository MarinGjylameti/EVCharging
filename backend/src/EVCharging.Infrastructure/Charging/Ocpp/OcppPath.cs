namespace EVCharging.Infrastructure.Charging.Ocpp;

// Some chargers append their identity to an already complete URL. Like the
// reference CSMS, resolve the last nonempty segment rather than a fixed prefix.
public static class OcppPath
{
    public static string? ExtractIdentity(string? path)
    {
        var parts = path?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is null || parts.Length == 0)
            return null;
        if (
            parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("health", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        // Known base addresses do not themselves identify a charger.
        if (
            parts.Length == 1
                && new[] { "ws", "cs", "ocpp" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
            || parts.Length == 2
                && parts[0].Equals("ws", StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals("cs", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        var identity = parts[^1];
        return IsValidIdentity(identity) ? identity : null;
    }

    public static bool IsValidIdentity(string identity) =>
        identity.Length is >= 1 and <= 40
        && identity.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

using Npgsql;

namespace EVCharging.Api;

public static class DatabaseConnection
{
    public static string Normalize(string connection)
    {
        if (
            !connection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
        )
            return connection;

        var uri = new Uri(connection, UriKind.Absolute);
        var credentials = uri.UserInfo.Split(':', 2);
        if (
            string.IsNullOrWhiteSpace(uri.Host)
            || uri.AbsolutePath.Length <= 1
            || credentials.Length != 2
            || credentials[0].Length == 0
            || credentials[1].Length == 0
        )
            throw new InvalidOperationException("The PostgreSQL URL is incomplete.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
        };

        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (
                var parameter in uri.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                var pair = parameter.Split('=', 2);
                if (
                    !pair[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                    || pair.Length != 2
                    || !Enum.TryParse<SslMode>(
                        Uri.UnescapeDataString(pair[1]),
                        true,
                        out var sslMode
                    )
                )
                    throw new InvalidOperationException(
                        "The PostgreSQL URL has an unsupported query parameter."
                    );
                builder.SslMode = sslMode;
            }
        }

        return builder.ConnectionString;
    }
}

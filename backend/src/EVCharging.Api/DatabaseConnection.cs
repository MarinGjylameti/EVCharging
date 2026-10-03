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
            // Password URLs do not require Kerberos. Avoid Npgsql's optional
            // GSSAPI probe in the Linux container; SSL settings stay independent.
            GssEncryptionMode = GssEncryptionMode.Disable,
        };

        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (
                var parameter in uri.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            )
            {
                var pair = parameter.Split('=', 2);
                if (pair.Length != 2)
                    throw new InvalidOperationException(
                        "The PostgreSQL URL has an unsupported query parameter."
                    );
                var value = Uri.UnescapeDataString(pair[1]);
                if (
                    pair[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<SslMode>(value, true, out var sslMode)
                    && Enum.IsDefined(sslMode)
                )
                    builder.SslMode = sslMode;
                else if (
                    pair[0].Equals("gssencmode", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<GssEncryptionMode>(value, true, out var gssMode)
                    && Enum.IsDefined(gssMode)
                )
                    builder.GssEncryptionMode = gssMode;
                else
                    throw new InvalidOperationException(
                        "The PostgreSQL URL has an unsupported query parameter."
                    );
            }
        }

        return builder.ConnectionString;
    }
}

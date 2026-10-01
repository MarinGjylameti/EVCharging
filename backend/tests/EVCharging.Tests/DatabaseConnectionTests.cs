using EVCharging.Api;
using Npgsql;

namespace EVCharging.Tests;

public class DatabaseConnectionTests
{
    [Fact]
    public void RenderPostgresUrlBecomesNpgsqlConnectionString()
    {
        var normalized = DatabaseConnection.Normalize(
            "postgresql://demo:p%40ss%3Bword@internal.example:5432/evcharging_demo?sslmode=require"
        );
        var parsed = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("internal.example", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("evcharging_demo", parsed.Database);
        Assert.Equal("demo", parsed.Username);
        Assert.Equal("p@ss;word", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void ExistingNpgsqlConnectionStringPassesThrough()
    {
        const string connection = "Host=127.0.0.1;Database=evcharging_dev;Username=evcharging";
        Assert.Equal(connection, DatabaseConnection.Normalize(connection));
    }
}

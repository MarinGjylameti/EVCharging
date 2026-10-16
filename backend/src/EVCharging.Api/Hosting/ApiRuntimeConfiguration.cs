namespace EVCharging.Api.Hosting;

public sealed class ApiRuntimeConfiguration
{
    private ApiRuntimeConfiguration(string? connectionString, string[] allowedOrigins)
    {
        ConnectionString = connectionString;
        AllowedOrigins = allowedOrigins;
    }

    public string? ConnectionString { get; }
    public string[] AllowedOrigins { get; }

    public static ApiRuntimeConfiguration Load(
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var configuredConnection =
            configuration.GetConnectionString("Default") ?? configuration["DATABASE_URL"];
        var connectionString = string.IsNullOrWhiteSpace(configuredConnection)
            ? null
            : DatabaseConnection.Normalize(configuredConnection);
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            ValidateProduction(connectionString, origins);

        // Features:TestMode is an explicit opt-in, independent of the host
        // environment. Legacy simulator settings alone cannot enable it.

        return new ApiRuntimeConfiguration(connectionString, origins);
    }

    private static void ValidateProduction(string? connectionString, string[] origins)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Production requires an explicit database connection string."
            );

        if (origins.Any(origin => !IsAllowedProductionOrigin(origin)))
            throw new InvalidOperationException("Production CORS origins must use HTTPS.");
    }

    private static bool IsAllowedProductionOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && !uri.IsLoopback;
}

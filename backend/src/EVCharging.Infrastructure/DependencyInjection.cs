using EVCharging.Application.Charging;
using EVCharging.Application.Notifications;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EVCharging.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChargingInfrastructure(
        this IServiceCollection services,
        string? connectionString
    )
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ChargingDbContext>(options =>
            options.UseNpgsql(
                connectionString
                    ?? "Host=127.0.0.1;Port=5440;Database=evcharging_dev;Username=evcharging;Timeout=3"
            )
        );

        AddIdentity(services);
        AddNotifications(services);
        services.AddScoped<IStationManagement, StationManagementService>();
        services.AddScoped<IDriverQueries, DriverQueries>();

        return services;
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
        services
            .AddOptions<SessionOptions>()
            .BindConfiguration("Sessions")
            .Validate(
                options => options.LifetimeHours is >= 1 and <= 168,
                "Session lifetime must be 1 to 168 hours."
            )
            .ValidateOnStart();
        services.AddScoped<AccountService>();
        services.AddScoped<SuperadminBootstrapper>();
    }

    private static void AddNotifications(IServiceCollection services)
    {
        services
            .AddOptions<PushOptions>()
            .BindConfiguration("Push")
            .Validate(
                options => options.MaxAttempts is >= 1 and <= 10,
                "Push retries must be 1 to 10."
            )
            .ValidateOnStart();
        services.AddScoped<NotificationService>();
        services.AddScoped<PushDispatcher>();
        services.AddHttpClient<IPushTransport, ExpoPushTransport>(client =>
            client.Timeout = TimeSpan.FromSeconds(20)
        );
        services.AddHostedService<PushWorker>();
    }
}

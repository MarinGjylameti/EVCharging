using EVCharging.Application.Charging;
using EVCharging.Application.Notifications;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Charging;
using EVCharging.Infrastructure.Charging.Ocpp;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChargingInfrastructure(
        this IServiceCollection services,
        string? connectionString
    )
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<RuntimeFeatures>().BindConfiguration("Features");
        services.AddSingleton<ApplicationDiagnostics>();
        services.AddSingleton<IApplicationDiagnostics>(p =>
            p.GetRequiredService<ApplicationDiagnostics>()
        );
        services
            .AddOptions<DiagnosticOptions>()
            .BindConfiguration("Diagnostics")
            .Validate(
                x =>
                    x.RetentionDays is >= 1 and <= 3650
                    && x.QueueCapacity is >= 200 and <= 100_000
                    && x.FlushSeconds is >= 1 and <= 30,
                "Invalid diagnostic retention, queue capacity, or flush interval."
            )
            .ValidateOnStart();
        services.AddScoped<ApplicationLogStore>();
        services.AddScoped<IApplicationLogHistory>(p =>
            p.GetRequiredService<ApplicationLogStore>()
        );
        services.AddHostedService<ApplicationLogWorker>();
        services.AddLogging(logging =>
            logging.AddFilter<OcppDiagnosticLoggerProvider>(level => level >= LogLevel.Information)
        );
        services.AddDbContext<ChargingDbContext>(options =>
            options.UseNpgsql(
                connectionString
                    ?? "Host=127.0.0.1;Port=5440;Database=evcharging_dev;Username=evcharging;Timeout=3"
            )
        );

        AddIdentity(services);
        AddNotifications(services);
        services.AddScoped<IStationManagement, StationManagementService>();
        services.AddScoped<OperationsAccess>();
        services.AddScoped<OperationsNotifications>();
        services.AddScoped<IOperationsManagement, OperationsManagementService>();
        services.AddScoped<IDriverQueries, DriverQueries>();
        services.AddScoped<IDriverCharging, DriverChargingService>();
        services.AddScoped<IChargerEventReceiver, ChargerEventReceiver>();
        services.AddScoped<StationProjection>();
        services.AddScoped<IChargingClaimVerification, DemoClaimVerification>();
        services.AddScoped<IChargingDiagnostics, OcppChargingDiagnostics>();
        services
            .AddOptions<OcppOptions>()
            .BindConfiguration("Ocpp")
            .PostConfigure<IOptions<RuntimeFeatures>>(
                (options, features) =>
                {
                    options.TestMode = features.Value.TestMode;
                    options.SimulatorEnabled &= options.TestMode;
                    options.AllowAnonymousSimulators &= options.SimulatorEnabled;
                }
            )
            .Validate(
                x =>
                    !x.SimulatorEnabled
                    || x.AllowAnonymousSimulators
                    || x.SimulatorPassword.Length is >= 12 and <= 128,
                "OCPP simulator mode requires a password between 12 and 128 characters."
            )
            .Validate(
                x =>
                    x.SimulatorIdentities.Length is >= 1 and <= 20
                    && x.SimulatorIdentities.All(id =>
                        System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,40}$")
                    ),
                "Invalid simulator identities."
            )
            .Validate(
                x =>
                    x.SimulatorTagId.Length is >= 1 and <= 20
                    && x.HeartbeatSeconds is >= 10 and <= 60
                    && x.OfflineSeconds >= x.HeartbeatSeconds * 2
                    && x.OfflineSeconds <= 180,
                "Invalid OCPP timing or tag configuration."
            )
            .ValidateOnStart();
        services.AddScoped<OcppSimulatorProvisioner>();
        services.AddScoped<OcppDeviceAuthentication>();
        services.AddScoped<OcppHardwareDiscovery>();
        services.AddScoped<OcppStationProjection>();
        services.AddScoped<OcppMeterHandler>();
        services.AddScoped<OcppTransactionHandler>();
        services.AddScoped<OcppActionHandler>();
        services.AddScoped<IOcppGateway, OcppGateway>();
        services.AddSingleton<StationEventBus>();
        services.AddSingleton<StationEventSignal>();
        services.AddSingleton<IStationEventStream>(provider =>
            provider.GetRequiredService<StationEventBus>()
        );
        services.AddHostedService<StationEventWorker>();
        services.AddHostedService<ChargerLivenessWorker>();

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

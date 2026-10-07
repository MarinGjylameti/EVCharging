using EVCharging.Api.Endpoints;
using EVCharging.Api.Hosting;
using EVCharging.Api.Ocpp;
using EVCharging.Api.Security;
using EVCharging.Api.Startup;
using EVCharging.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("EVCHARGING_");
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var configuration = ApiRuntimeConfiguration.Load(builder.Configuration, builder.Environment);
builder.Services.AddChargingInfrastructure(configuration.ConnectionString);
builder.Services.AddApiSecurity();
builder.Services.AddSingleton<OcppConnectionRegistry>();
builder.ConfigureApiHttp(configuration.AllowedOrigins);

var app = builder.Build();
app.UseApiPipeline();
app.MapHealthEndpoints();
app.MapAccountEndpoints();
app.MapDriverEndpoints();
app.MapStationManagementEndpoints();
app.MapOperationsEndpoints();
app.MapApplicationLogEndpoints();
app.MapOcppEndpoints();
app.MapStationEventsEndpoints();

if (await app.TryRunStartupCommandAsync(args))
    return;

await app.InitializeOcppSimulatorsAsync();
app.Lifetime.ApplicationStarted.Register(() =>
    app
        .Services.GetRequiredService<EVCharging.Application.Charging.IApplicationDiagnostics>()
        .Record(
            "System",
            "Information",
            "API is ready to accept requests.",
            context: new(EventName: "process_ready")
        )
);
app.Lifetime.ApplicationStopping.Register(() =>
    app
        .Services.GetRequiredService<EVCharging.Application.Charging.IApplicationDiagnostics>()
        .Record(
            "System",
            "Information",
            "Host shutdown requested; active connections may disconnect.",
            context: new(EventName: "shutdown_requested")
        )
);
app.Run();

public partial class Program;

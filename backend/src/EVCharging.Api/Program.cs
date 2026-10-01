using EVCharging.Api.Endpoints;
using EVCharging.Api.Hosting;
using EVCharging.Api.Security;
using EVCharging.Api.Startup;
using EVCharging.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("EVCHARGING_");

var configuration = ApiRuntimeConfiguration.Load(builder.Configuration, builder.Environment);
builder.Services.AddChargingInfrastructure(configuration.ConnectionString);
builder.Services.AddApiSecurity();
builder.ConfigureApiHttp(configuration.AllowedOrigins);

var app = builder.Build();
app.UseApiPipeline();
app.MapHealthEndpoints();
app.MapAccountEndpoints();
app.MapDriverEndpoints();
app.MapStationManagementEndpoints();

if (await app.TryRunStartupCommandAsync(args))
    return;

app.Run();

public partial class Program;

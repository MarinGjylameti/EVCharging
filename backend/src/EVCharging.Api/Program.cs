using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EVCharging.Api;
using EVCharging.Api.Endpoints;
using EVCharging.Api.Security;
using EVCharging.Application.Notifications;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("EVCHARGING_");
var configuredConnection = builder.Configuration.GetConnectionString("Default") ?? builder.Configuration["DATABASE_URL"];
var connectionString = string.IsNullOrWhiteSpace(configuredConnection)
    ? null : DatabaseConnection.Normalize(configuredConnection);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32_768);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ChargingDbContext>(options =>
    options.UseNpgsql(connectionString
        ?? "Host=127.0.0.1;Port=5440;Database=evcharging_dev;Username=evcharging;Timeout=3"));
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddOptions<EVCharging.Infrastructure.Identity.SessionOptions>().BindConfiguration("Sessions")
    .Validate(x => x.LifetimeHours is >= 1 and <= 168, "Session lifetime must be 1 to 168 hours.").ValidateOnStart();
builder.Services.AddOptions<PushOptions>().BindConfiguration("Push")
    .Validate(x => x.MaxAttempts is >= 1 and <= 10, "Push retries must be 1 to 10.").ValidateOnStart();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PushDispatcher>();
builder.Services.AddHttpClient<IPushTransport, ExpoPushTransport>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHostedService<PushWorker>();
builder.Services.AddAuthentication(SessionAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthentication>(SessionAuthentication.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Operator", p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin), nameof(UserRole.Superadmin)));
    options.AddPolicy("Superadmin", p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Superadmin)));
    options.AddPolicy("Driver", p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Driver)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Production requires an explicit database connection string.");
    if (origins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback))
        throw new InvalidOperationException("Production CORS origins must use HTTPS.");
}
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
var app = builder.Build();
app.UseMiddleware<ApiErrors>();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (ChargingDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapAccountEndpoints();
app.MapDriverEndpoints();
app.MapStationManagementEndpoints();

if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ChargingDbContext>().Database.MigrateAsync();
    return;
}
if (args.Contains("--bootstrap-superadmin"))
{
    var email = EVCharging.Application.Identity.AccountRules.NormalizeEmail(builder.Configuration["Bootstrap:Email"]);
    var password = builder.Configuration["Bootstrap:Password"] ?? "";
    EVCharging.Application.Identity.AccountRules.ValidatePassword(password);
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ChargingDbContext>();
    if (await db.Accounts.AnyAsync(x => x.Role == UserRole.Superadmin))
        throw new InvalidOperationException("A Superadmin already exists. Bootstrap will not alter it.");
    var account = new Account { Email = builder.Configuration["Bootstrap:Email"]!.Trim(), NormalizedEmail = email, FirstName = "System", LastName = "Administrator", Role = UserRole.Superadmin };
    account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(account, password);
    db.Accounts.Add(account);
    db.Audit.Add(new AuditEntry { ActorId = account.Id, ActorRole = "Superadmin", EntityId = account.Id, Action = "SuperadminBootstrapped" });
    await db.SaveChangesAsync();
    return;
}
if (args.Contains("--seed-demo"))
{
    await DemoDataSeeder.SeedAsync(app.Services, builder.Configuration);
    return;
}
app.Run();
public partial class Program;

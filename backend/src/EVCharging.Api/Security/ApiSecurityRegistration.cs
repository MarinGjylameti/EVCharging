using System.Threading.RateLimiting;
using EVCharging.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EVCharging.Api.Security;

public static class ApiSecurityRegistration
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services
            .AddOptions<AuthLimitOptions>()
            .BindConfiguration("AuthLimits")
            .Validate(
                x =>
                    x.AccountAttemptsPerMinute is >= 1 and <= 100
                    && x.IpRequestsPerMinute >= x.AccountAttemptsPerMinute
                    && x.IpRequestsPerMinute <= 10000,
                "Invalid authentication rate limits."
            )
            .ValidateOnStart();
        services.AddSingleton<AuthAttemptLimiter>();
        services
            .AddAuthentication(SessionAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthentication>(
                SessionAuthentication.SchemeName,
                _ => { }
            );
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "Operator",
                policy =>
                    policy
                        .RequireAuthenticatedUser()
                        .RequireRole(nameof(UserRole.Admin), nameof(UserRole.Superadmin))
            );
            options.AddPolicy(
                "Superadmin",
                policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Superadmin))
            );
            options.AddPolicy(
                "Driver",
                policy => policy.RequireAuthenticatedUser().RequireRole(nameof(UserRole.Driver))
            );
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, _) =>
                await AuthAttemptLimiter
                    .Rejected(context.HttpContext, context.Lease)
                    .ExecuteAsync(context.HttpContext);
            options.AddPolicy(
                "device-connect",
                context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            );
            options.AddPolicy(
                "auth",
                context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            // A web BFF shares its egress IP across many users. This
                            // is an aggregate abuse ceiling; the filter also limits
                            // each normalized account. Forwarded headers are untrusted.
                            PermitLimit = context
                                .RequestServices.GetRequiredService<IOptions<AuthLimitOptions>>()
                                .Value.IpRequestsPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            );
        });

        return services;
    }
}

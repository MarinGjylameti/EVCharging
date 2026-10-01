using System.Threading.RateLimiting;
using EVCharging.Domain.Identity;
using Microsoft.AspNetCore.Authentication;

namespace EVCharging.Api.Security;

public static class ApiSecurityRegistration
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
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
            options.AddPolicy(
                "auth",
                context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 15,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            );
        });

        return services;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using EVCharging.Application.Identity;
using Microsoft.Extensions.Options;

namespace EVCharging.Api.Security;

public sealed class AuthLimitOptions
{
    public int AccountAttemptsPerMinute { get; set; } = 15;
    public int IpRequestsPerMinute { get; set; } = 300;
}

// Shared by web and native clients, including requests from different IPs.
// Partition keys contain a hash rather than an email, password or bearer token.
public sealed class AuthAttemptLimiter(IOptions<AuthLimitOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<
        string,
        string
    >(key =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.Value.AccountAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }
        )
    );

    public RateLimitLease Attempt(string key) => limiter.AttemptAcquire(key);

    public void Dispose() => limiter.Dispose();

    public static string EmailKey(string email) =>
        "email:"
        + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(AccountRules.NormalizeEmail(email)))
        );

    public static IResult Rejected(HttpContext context, RateLimitLease lease)
    {
        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
            : 60;
        context.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
        return Results.Json(
            new { code = "rate_limited", traceId = context.TraceIdentifier },
            statusCode: 429
        );
    }
}

public sealed class AuthAttemptFilter(AuthAttemptLimiter limiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var key = context.Arguments.FirstOrDefault(argument =>
            argument is LoginRequest or RegisterRequest or PasswordRequest
        ) switch
        {
            LoginRequest request => AuthAttemptLimiter.EmailKey(request.Email),
            RegisterRequest request => AuthAttemptLimiter.EmailKey(request.Email),
            PasswordRequest => "password:" + context.HttpContext.User.UserId(),
            _ => null,
        };
        // Logout always remains available to revoke an authenticated session.
        if (key is null)
            return await next(context);
        using var lease = limiter.Attempt(key);
        return lease.IsAcquired
            ? await next(context)
            : AuthAttemptLimiter.Rejected(context.HttpContext, lease);
    }
}

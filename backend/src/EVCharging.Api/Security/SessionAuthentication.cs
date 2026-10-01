using System.Security.Claims;
using System.Text.Encodings.Web;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Api.Security;

public sealed class SessionAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ChargingDbContext db, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpaqueSession";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) return AuthenticateResult.Fail("Invalid session.");
        var hash = AccountService.HashToken(token);
        var session = await db.Sessions.AsNoTracking().Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), Context.RequestAborted);
        if (session is null || !session.User.IsActive || session.User.ArchivedAt is not null)
            return AuthenticateResult.Fail("Session has expired.");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new Claim(ClaimTypes.Role, session.User.Role.ToString()),
            new Claim("session_id", session.Id.ToString())
        };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}

public static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static Guid SessionId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("session_id")!);
}

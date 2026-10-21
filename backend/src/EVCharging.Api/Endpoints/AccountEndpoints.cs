using System.Security.Claims;
using EVCharging.Api.Security;
using EVCharging.Application.Identity;
using EVCharging.Infrastructure.Identity;
using EVCharging.Infrastructure.Notifications;

namespace EVCharging.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").AddEndpointFilter<AuthAttemptFilter>();
        auth.MapPost(
                "/register",
                async (RegisterRequest request, AccountService accounts, CancellationToken ct) =>
                    Results.Ok(await accounts.RegisterAsync(request, ct))
            )
            .RequireRateLimiting("auth");
        auth.MapPost(
                "/login",
                async (LoginRequest request, AccountService accounts, CancellationToken ct) =>
                    Results.Ok(await accounts.LoginAsync(request, ct))
            )
            .RequireRateLimiting("auth");
        auth.MapPost(
                "/logout",
                async (ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
                {
                    await accounts.LogoutAsync(user.UserId(), user.SessionId(), ct);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization();
        auth.MapPost(
                "/password",
                async (
                    PasswordRequest request,
                    ClaimsPrincipal user,
                    AccountService accounts,
                    CancellationToken ct
                ) =>
                {
                    await accounts.ChangePasswordAsync(user.UserId(), request, ct);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization()
            .RequireRateLimiting("auth");

        var me = app.MapGroup("/api/v1/me").RequireAuthorization();
        me.MapGet(
            "",
            async (ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
                Results.Ok(AccountRules.ToDto(await accounts.GetAsync(user.UserId(), ct)))
        );
        me.MapPatch(
            "/profile",
            async (
                ProfileRequest request,
                ClaimsPrincipal user,
                AccountService accounts,
                CancellationToken ct
            ) => Results.Ok(await accounts.UpdateProfileAsync(user.UserId(), request, ct))
        );
        me.MapPatch(
            "/settings",
            async (
                SettingsRequest request,
                ClaimsPrincipal user,
                AccountService accounts,
                CancellationToken ct
            ) => Results.Ok(await accounts.UpdateSettingsAsync(user.UserId(), request, ct))
        );
        me.MapPost(
            "/devices",
            async (
                DeviceRequest request,
                ClaimsPrincipal user,
                NotificationService notifications,
                CancellationToken ct
            ) =>
            {
                await notifications.RegisterDeviceAsync(
                    user.UserId(),
                    user.SessionId(),
                    request,
                    ct
                );
                return Results.NoContent();
            }
        );
        me.MapDelete(
            "/devices",
            async (ClaimsPrincipal user, NotificationService notifications, CancellationToken ct) =>
            {
                await notifications.UnregisterDeviceAsync(user.UserId(), user.SessionId(), ct);
                return Results.NoContent();
            }
        );
        me.MapGet(
            "/notifications",
            async (ClaimsPrincipal user, NotificationService notifications, CancellationToken ct) =>
                Results.Ok(await notifications.ListForUserAsync(user.UserId(), ct))
        );

        app.MapGet(
                "/api/v1/admin/access",
                (ClaimsPrincipal user) =>
                    Results.Ok(new { userId = user.UserId(), workspace = "operations" })
            )
            .RequireAuthorization("Operator");
        app.MapGet(
                "/api/v1/administrators",
                async (AccountService accounts, CancellationToken ct) =>
                    Results.Ok(await accounts.ListAdministratorsAsync(ct))
            )
            .RequireAuthorization("Superadmin");
    }
}

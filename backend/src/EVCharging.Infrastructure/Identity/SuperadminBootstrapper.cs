using EVCharging.Application.Identity;
using EVCharging.Domain.Auditing;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Identity;

public sealed class SuperadminBootstrapper(ChargingDbContext db, IPasswordHasher<Account> hasher)
{
    public async Task BootstrapAsync(
        string? email,
        string? password,
        CancellationToken ct = default
    )
    {
        var normalizedEmail = AccountRules.NormalizeEmail(email);
        password ??= "";
        AccountRules.ValidatePassword(password);

        if (await db.Accounts.AnyAsync(account => account.Role == UserRole.Superadmin, ct))
            throw new InvalidOperationException(
                "A Superadmin already exists. Bootstrap will not alter it."
            );

        var account = new Account
        {
            Email = email!.Trim(),
            NormalizedEmail = normalizedEmail,
            FirstName = "System",
            LastName = "Administrator",
            Role = UserRole.Superadmin,
        };
        account.PasswordHash = hasher.HashPassword(account, password);

        db.Accounts.Add(account);
        db.Audit.Add(
            new AuditEntry
            {
                ActorId = account.Id,
                ActorRole = "Superadmin",
                EntityId = account.Id,
                Action = "SuperadminBootstrapped",
            }
        );
        await db.SaveChangesAsync(ct);
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;

namespace TunnelHub.Server.Account;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

/// <summary>Registration rules: first user becomes Admin, everyone else needs a valid invite code.</summary>
public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    ILogger<AccountService> logger)
{
    public sealed record RegisterResult(bool Succeeded, ApplicationUser? User, string? Error, bool IsAdmin);

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? inviteCode)
    {
        var isFirstUser = !await userManager.Users.AnyAsync();

        InviteCode? invite = null;
        if (!isFirstUser)
        {
            var code = inviteCode?.Trim();
            if (string.IsNullOrEmpty(code))
                return new(false, null, "An invite code is required to register.", false);

            invite = await db.InviteCodes.FirstOrDefaultAsync(i => i.Code == code);
            if (invite is null || !invite.IsUsable(DateTimeOffset.UtcNow))
                return new(false, null, "That invite code is invalid, expired, or already used.", false);
        }

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var create = await userManager.CreateAsync(user, password);
        if (!create.Succeeded)
            return new(false, null, string.Join(" ", create.Errors.Select(e => e.Description)), false);

        var role = isFirstUser ? Roles.Admin : Roles.User;
        await userManager.AddToRoleAsync(user, role);

        if (invite is not null)
        {
            invite.RedeemedByUserId = user.Id;
            invite.RedeemedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Registered {Email} as {Role}", email, role);
        return new(true, user, null, isFirstUser);
    }
}

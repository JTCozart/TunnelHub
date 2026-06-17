using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;
using Ztpr.Server.Services;

namespace Ztpr.Server.Account;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}

/// <summary>
/// Registration rules: the first user always becomes Admin. After that, an invite code
/// is required only when the admin has left the requirement on (the default); when it is
/// turned off, anyone may self-register.
/// </summary>
public sealed class AccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    SettingsService settings,
    ILogger<AccountService> logger)
{
    public sealed record RegisterResult(
        bool Succeeded, ApplicationUser? User, string? Error, bool IsAdmin, bool RequiresEmailConfirmation);

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? inviteCode)
    {
        var isFirstUser = !await userManager.Users.AnyAsync();

        // The first user (admin) is always confirmed; everyone else must confirm by email
        // when the policy is on and email is actually configured.
        var requireConfirmation = !isFirstUser
            && settings.Current.RequireEmailConfirmation
            && settings.Current.HasEmail;

        InviteCode? invite = null;
        if (!isFirstUser && settings.Current.RequireInviteCode)
        {
            var code = inviteCode?.Trim();
            if (string.IsNullOrEmpty(code))
                return new(false, null, "An invite code is required to register.", false, false);

            invite = await db.InviteCodes.FirstOrDefaultAsync(i => i.Code == code);
            if (invite is null || !invite.IsUsable(DateTimeOffset.UtcNow))
                return new(false, null, "That invite code is invalid, expired, or already used.", false, false);
        }

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = !requireConfirmation };
        var create = await userManager.CreateAsync(user, password);
        if (!create.Succeeded)
            return new(false, null, string.Join(" ", create.Errors.Select(e => e.Description)), false, false);

        var role = isFirstUser ? Roles.Admin : Roles.User;
        await userManager.AddToRoleAsync(user, role);

        if (invite is not null)
        {
            invite.RedeemedByUserId = user.Id;
            invite.RedeemedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Registered {Email} as {Role}", email, role);
        return new(true, user, null, isFirstUser, requireConfirmation);
    }
}

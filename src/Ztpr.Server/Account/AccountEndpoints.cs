using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Ztpr.Server.Data.Entities;
using Ztpr.Server.Services;

namespace Ztpr.Server.Account;

/// <summary>
/// Minimal-API form endpoints for cookie auth. These run in the HTTP request
/// context (not a Blazor circuit) so they can set the Identity auth cookie. The
/// SSR Login/Register pages post HTML forms here.
/// </summary>
public static class AccountEndpoints
{
    private const string LockedMessage =
        "This account is locked after too many failed backup-code attempts. Contact an administrator.";

    /// <summary>Name of the per-IP rate-limit policy applied to every auth endpoint (see Program.cs).</summary>
    public const string RateLimitPolicy = "auth";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/account").RequireRateLimiting(RateLimitPolicy);

        group.MapPost("/register", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string? inviteCode,
            HttpContext http,
            AccountService accounts,
            AccountEmailService accountEmail,
            AuditLogService audit,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var result = await accounts.RegisterAsync(email, password, inviteCode);
            if (!result.Succeeded)
                return Results.Redirect($"/register?error={Uri.EscapeDataString(result.Error!)}");

            await audit.LogAsync(AuditEventType.UserCreated, result.User!.Id, result.User.Email, Ip(http),
                detail: result.IsAdmin ? "first user — administrator" : "self-registered");

            // Email confirmation required: send the verification link and tell the user to
            // check their inbox instead of signing them in.
            if (result.RequiresEmailConfirmation)
            {
                var send = await accountEmail.SendConfirmationAsync(result.User!, BaseUrl(http));
                await audit.LogAsync(AuditEventType.EmailConfirmationSent, result.User!.Id, result.User.Email, Ip(http),
                    send.Succeeded ? null : $"send failed: {send.Error}");
                return Results.Redirect($"/register?registered={Uri.EscapeDataString(result.User!.Email!)}");
            }

            await signInManager.SignInAsync(result.User!, isPersistent: true);
            return Results.Redirect("/");
        });

        // Email confirmation link target. Confirms the address, then signs the user in.
        group.MapGet("/confirm-email", async (
            string userId,
            string code,
            HttpContext http,
            AuditLogService audit,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return LoginError("That confirmation link is no longer valid. Please sign in or register again.");

            string token;
            try { token = AccountEmailService.DecodeToken(code); }
            catch { return LoginError("That confirmation link is malformed."); }

            var result = await userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
                return LoginError("That confirmation link is invalid or has expired. Try signing in to request a new one.");

            await audit.LogAsync(AuditEventType.EmailConfirmed, user.Id, user.Email, Ip(http));
            if (user.IsBlocked || user.IsLocked)
                return Results.Redirect("/login?error=" + Uri.EscapeDataString("Email confirmed. You can sign in once your account is unlocked."));

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Redirect("/");
        });

        // Resend a confirmation email. Enumeration-safe: always reports the same result.
        group.MapPost("/resend-confirmation", async (
            [FromForm] string email,
            HttpContext http,
            AccountEmailService accountEmail,
            AuditLogService audit,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is not null && !user.EmailConfirmed && accountEmail.IsConfigured)
            {
                var send = await accountEmail.SendConfirmationAsync(user, BaseUrl(http));
                await audit.LogAsync(AuditEventType.EmailConfirmationSent, user.Id, user.Email, Ip(http),
                    send.Succeeded ? "resend" : $"resend failed: {send.Error}");
            }
            return Results.Redirect($"/login?info={Uri.EscapeDataString("If that account needs confirmation, we've sent a new link.")}");
        });

        group.MapPost("/login", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            HttpContext http,
            AuditLogService audit,
            SettingsService settings,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
                return await Failed(audit, http, email, null, "Invalid email or password.");
            if (user.IsBlocked)
                return await Failed(audit, http, email, user.Id, "This account has been blocked.");
            if (user.IsLocked)
                return await Failed(audit, http, email, user.Id, LockedMessage);
            // Block sign-in until the address is confirmed, when that policy is on. Only after
            // a correct password so we don't reveal which accounts exist or are unconfirmed.
            if (!user.EmailConfirmed && settings.Current.RequireEmailConfirmation && settings.Current.HasEmail
                && await userManager.CheckPasswordAsync(user, password))
                return Results.Redirect("/login?error=" +
                    Uri.EscapeDataString("Please confirm your email address before signing in.") + "&unconfirmed=1");

            var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: true);
            if (result.RequiresTwoFactor)
                return Results.Redirect($"/login-mfa{ReturnQuery(returnUrl)}");
            if (result.IsLockedOut)
                return await Failed(audit, http, email, user.Id,
                    "Too many failed attempts. This account is temporarily locked — try again later.");
            if (!result.Succeeded)
                return await Failed(audit, http, email, user.Id, "Invalid email or password.");

            await audit.LogAsync(AuditEventType.LoginSucceeded, user.Id, user.Email, Ip(http), "password");
            return RedirectToDest(returnUrl);
        });

        // Second factor: authenticator (TOTP) code. The TwoFactorUserId cookie set during
        // the password step identifies the half-authenticated user. TOTP failures are not
        // rate-limited (only backup codes are, per policy).
        group.MapPost("/login-mfa", async (
            [FromForm] string code,
            [FromForm] string? returnUrl,
            HttpContext http,
            AuditLogService audit,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return LoginError("Your sign-in session expired. Please sign in again.");

            var token = (code ?? "").Replace(" ", "").Replace("-", "");
            var result = await signInManager.TwoFactorAuthenticatorSignInAsync(token, isPersistent: true, rememberClient: false);
            if (!result.Succeeded)
            {
                await audit.LogAsync(AuditEventType.LoginFailed, user.Id, user.Email, Ip(http), "invalid authenticator code");
                return MfaError("Invalid authenticator code.", returnUrl, recovery: false);
            }

            await audit.LogAsync(AuditEventType.LoginSucceeded, user.Id, user.Email, Ip(http), "authenticator");
            return RedirectToDest(returnUrl);
        });

        // Second factor: one-time backup (recovery) code. A valid code logs the user in once
        // and is consumed; MFA stays enabled. Five consecutive failures lock the account.
        group.MapPost("/login-mfa-recovery", async (
            [FromForm] string code,
            [FromForm] string? returnUrl,
            HttpContext http,
            AuditLogService audit,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return LoginError("Your sign-in session expired. Please sign in again.");

            var token = (code ?? "").Replace(" ", "").Replace("-", "");
            var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(token);
            if (result.Succeeded)
            {
                if (user.BackupCodeFailedCount != 0)
                {
                    user.BackupCodeFailedCount = 0;
                    await userManager.UpdateAsync(user);
                }
                await audit.LogAsync(AuditEventType.LoginSucceeded, user.Id, user.Email, Ip(http), "backup code");
                return RedirectToDest(returnUrl);
            }

            user.BackupCodeFailedCount++;
            if (user.BackupCodeFailedCount >= Mfa.MaxBackupCodeFailures)
            {
                user.IsLocked = true;
                user.LockedAt = DateTimeOffset.UtcNow;
                await userManager.UpdateAsync(user);
                // Invalidate the half-authenticated session and any live cookies.
                await userManager.UpdateSecurityStampAsync(user);
                await signInManager.SignOutAsync();
                await audit.LogAsync(AuditEventType.LoginFailed, user.Id, user.Email, Ip(http),
                    "backup code — account locked after too many failures");
                return LoginError(LockedMessage);
            }

            await userManager.UpdateAsync(user);
            await audit.LogAsync(AuditEventType.LoginFailed, user.Id, user.Email, Ip(http), "invalid backup code");
            var remaining = Mfa.MaxBackupCodeFailures - user.BackupCodeFailedCount;
            return MfaError($"Invalid backup code. {remaining} attempt(s) remaining before lockout.", returnUrl, recovery: true);
        });

        // Change the signed-in user's password. Runs in HTTP context (not a circuit) so it can
        // RefreshSignInAsync after ChangePasswordAsync rotates the security stamp — otherwise the
        // user's current cookie would be invalidated and they'd be signed out.
        group.MapPost("/change-password", async (
            [FromForm] string currentPassword,
            [FromForm] string newPassword,
            [FromForm] string confirmPassword,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.GetUserAsync(http.User);
            if (user is null)
                return Results.Redirect("/login");

            if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
                return PasswordError("The new password and confirmation don't match.");

            var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
                return PasswordError(string.Join(" ", result.Errors.Select(e => e.Description)));

            await signInManager.RefreshSignInAsync(user);
            return Results.Redirect("/account/security?pwChanged=1");
        });

        // Request a password-reset link. Enumeration-safe: always redirects to the same
        // "check your email" page whether or not the address matches an account.
        group.MapPost("/forgot-password", async (
            [FromForm] string email,
            HttpContext http,
            AccountEmailService accountEmail,
            AuditLogService audit,
            UserManager<ApplicationUser> userManager) =>
        {
            if (!accountEmail.IsConfigured)
                return Results.Redirect("/forgot-password?error=" +
                    Uri.EscapeDataString("Password reset by email isn't available on this server."));

            var user = await userManager.FindByEmailAsync(email);
            // Only email confirmed, active accounts — but never reveal which case applied.
            if (user is { IsBlocked: false } && user.EmailConfirmed)
            {
                var send = await accountEmail.SendPasswordResetAsync(user, BaseUrl(http));
                await audit.LogAsync(AuditEventType.PasswordResetRequested, user.Id, user.Email, Ip(http),
                    send.Succeeded ? null : $"send failed: {send.Error}");
            }
            return Results.Redirect($"/forgot-password?sent={Uri.EscapeDataString(email)}");
        });

        // Apply a new password using the token from the reset email.
        group.MapPost("/reset-password", async (
            [FromForm] string userId,
            [FromForm] string code,
            [FromForm] string newPassword,
            [FromForm] string confirmPassword,
            HttpContext http,
            AuditLogService audit,
            UserManager<ApplicationUser> userManager) =>
        {
            string Back(string message) =>
                $"/reset-password?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}&error={Uri.EscapeDataString(message)}";

            if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
                return Results.Redirect(Back("The new password and confirmation don't match."));

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return LoginError("That reset link is no longer valid. Request a new one.");

            string token;
            try { token = AccountEmailService.DecodeToken(code); }
            catch { return Results.Redirect(Back("That reset link is malformed.")); }

            var result = await userManager.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
                return Results.Redirect(Back(string.Join(" ", result.Errors.Select(e => e.Description))));

            // Rotate the stamp so any other live sessions are invalidated after a reset.
            await userManager.UpdateSecurityStampAsync(user);
            await audit.LogAsync(AuditEventType.PasswordResetCompleted, user.Id, user.Email, Ip(http));
            return Results.Redirect("/login?info=" +
                Uri.EscapeDataString("Your password has been reset. You can now sign in."));
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    /// <summary>Absolute origin of the current request (scheme + host[:port]) for building email links.</summary>
    private static string BaseUrl(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

    // Records a failed-login audit entry, then redirects back to the login page with the message.
    private static async Task<IResult> Failed(
        AuditLogService audit, HttpContext http, string email, string? userId, string message)
    {
        await audit.LogAsync(AuditEventType.LoginFailed, userId, email, Ip(http), message);
        return LoginError(message);
    }

    private static string ReturnQuery(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}";

    private static IResult RedirectToDest(string? returnUrl) =>
        Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);

    private static IResult LoginError(string message) =>
        Results.Redirect($"/login?error={Uri.EscapeDataString(message)}");

    private static IResult PasswordError(string message) =>
        Results.Redirect($"/account/security?pwError={Uri.EscapeDataString(message)}");

    private static IResult MfaError(string message, string? returnUrl, bool recovery)
    {
        var qs = $"?error={Uri.EscapeDataString(message)}";
        if (recovery) qs += "&recovery=1";
        if (!string.IsNullOrWhiteSpace(returnUrl)) qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.Redirect($"/login-mfa{qs}");
    }
}

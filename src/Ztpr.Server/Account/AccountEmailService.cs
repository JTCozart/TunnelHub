using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using Ztpr.Server.Data.Entities;
using Ztpr.Server.Services;

namespace Ztpr.Server.Account;

/// <summary>
/// Builds and sends the account-lifecycle emails (address confirmation, password reset).
/// Tokens come from ASP.NET Core Identity's default token providers and are URL-encoded
/// into absolute links built from the request's own scheme/host, so they work regardless
/// of port or deployment host.
/// </summary>
public sealed class AccountEmailService(
    UserManager<ApplicationUser> userManager,
    EmailSender email,
    ILogger<AccountEmailService> logger)
{
    public bool IsConfigured => email.IsConfigured;

    /// <summary>Generate a confirmation token and email a confirm link. Returns the send result.</summary>
    public async Task<EmailResult> SendConfirmationAsync(ApplicationUser user, string baseUrl, CancellationToken ct = default)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{baseUrl}/account/confirm-email?userId={Uri.EscapeDataString(user.Id)}&code={encoded}";

        var html = Wrap("Confirm your email",
            $"""
             <p>Thanks for registering. Please confirm your email address to activate your account.</p>
             <p style="margin:24px 0;"><a href="{WebUtility.HtmlEncode(link)}"
                style="background:#2563eb;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;">Confirm email</a></p>
             <p>If the button doesn't work, paste this link into your browser:</p>
             <p style="word-break:break-all;"><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(link)}</a></p>
             <p style="color:#666;">If you didn't create this account, you can ignore this email.</p>
             """);

        var result = await email.SendAsync(user.Email!, "Confirm your email", html, ct: ct);
        if (!result.Succeeded)
            logger.LogWarning("Failed to send confirmation email to {Email}: {Error}", user.Email, result.Error);
        return result;
    }

    /// <summary>Generate a reset token and email a reset link. Returns the send result.</summary>
    public async Task<EmailResult> SendPasswordResetAsync(ApplicationUser user, string baseUrl, CancellationToken ct = default)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{baseUrl}/reset-password?userId={Uri.EscapeDataString(user.Id)}&code={encoded}";

        var html = Wrap("Reset your password",
            $"""
             <p>We received a request to reset the password for your account. This link expires shortly.</p>
             <p style="margin:24px 0;"><a href="{WebUtility.HtmlEncode(link)}"
                style="background:#2563eb;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;">Reset password</a></p>
             <p>If the button doesn't work, paste this link into your browser:</p>
             <p style="word-break:break-all;"><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(link)}</a></p>
             <p style="color:#666;">If you didn't request this, you can safely ignore this email — your password won't change.</p>
             """);

        var result = await email.SendAsync(user.Email!, "Reset your password", html, ct: ct);
        if (!result.Succeeded)
            logger.LogWarning("Failed to send password-reset email to {Email}: {Error}", user.Email, result.Error);
        return result;
    }

    /// <summary>Decode a Base64Url token previously produced by this service.</summary>
    public static string DecodeToken(string code) =>
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));

    private static string Wrap(string heading, string body) =>
        $"""
         <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:520px;margin:auto;color:#111;">
           <h2 style="font-weight:600;">{WebUtility.HtmlEncode(heading)}</h2>
           {body}
           <hr style="border:none;border-top:1px solid #eee;margin:24px 0;" />
           <p style="color:#999;font-size:12px;">Sent by Ztpr.</p>
         </div>
         """;
}

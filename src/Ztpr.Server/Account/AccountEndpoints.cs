using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Account;

/// <summary>
/// Minimal-API form endpoints for cookie auth. These run in the HTTP request
/// context (not a Blazor circuit) so they can set the Identity auth cookie. The
/// SSR Login/Register pages post HTML forms here.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/account");

        group.MapPost("/register", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string? inviteCode,
            AccountService accounts,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var result = await accounts.RegisterAsync(email, password, inviteCode);
            if (!result.Succeeded)
                return Results.Redirect($"/register?error={Uri.EscapeDataString(result.Error!)}");

            await signInManager.SignInAsync(result.User!, isPersistent: true);
            return Results.Redirect("/");
        });

        group.MapPost("/login", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
                return LoginError("Invalid email or password.");
            if (user.IsBlocked)
                return LoginError("This account has been blocked.");

            var result = await signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
            if (!result.Succeeded)
                return LoginError("Invalid email or password.");

            var dest = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            return Results.LocalRedirect(dest);
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }

    private static IResult LoginError(string message) =>
        Results.Redirect($"/login?error={Uri.EscapeDataString(message)}");
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Account;

/// <summary>
/// Offline maintenance commands run from the server binary instead of the web UI — used
/// to recover an account (typically the admin) that has locked itself out of MFA. These
/// build a minimal service provider and never start Kestrel, so no ports are bound.
///
/// Usage: <c>Ztpr.Server reset-mfa &lt;email&gt;</c>
/// Stop the service first to avoid SQLite write contention.
/// </summary>
public static class AdminMaintenance
{
    public const string ResetMfaCommand = "reset-mfa";

    /// <summary>Disable MFA, clear the authenticator key, and clear any MFA lockout for a user.</summary>
    public static async Task<int> ResetMfaAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Usage: Ztpr.Server {ResetMfaCommand} <email>");
            return 1;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var dbPath = config.GetConnectionString("Sqlite") ?? "Data Source=ztpr.db";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(dbPath));
        services.AddScoped<AppDbContext>(p => p.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            Console.Error.WriteLine($"No user found with email '{email}'.");
            return 1;
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        // Drops the authenticator shared key. Any old recovery codes are inert once 2FA is off.
        await users.ResetAuthenticatorKeyAsync(user);

        user.IsLocked = false;
        user.LockedAt = null;
        user.BackupCodeFailedCount = 0;
        await users.UpdateAsync(user);
        // Sign the user out of any live sessions/cookies.
        await users.UpdateSecurityStampAsync(user);

        Console.WriteLine($"MFA disabled and account unlocked for '{email}'. They can now sign in with their password.");
        return 0;
    }
}

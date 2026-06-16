using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Account;

namespace TunnelHub.Server.Data;

public static class DbInitializer
{
    /// <summary>Apply pending migrations and ensure the Admin/User roles exist.</summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Admin, Roles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

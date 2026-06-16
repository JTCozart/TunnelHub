using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Configuration;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Data;

/// <summary>
/// Reads the singleton <see cref="AdminSettings"/> row before the web host is built, so
/// startup-only decisions (whether to bind the HTTPS listener, and the app host used for
/// the HTTP→HTTPS redirect) can come from runtime configuration rather than appsettings.
/// Creates and seeds the row on first run, honoring any legacy appsettings values.
/// </summary>
public static class SettingsBootstrap
{
    public static async Task<AdminSettings> LoadAsync(string connectionString, IConfiguration config)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var row = await db.AdminSettings.FirstOrDefaultAsync(s => s.Id == AdminSettings.SingletonId);
        if (row is null)
        {
            row = new AdminSettings
            {
                Id = AdminSettings.SingletonId,
                // First-run seed: fall back to any legacy appsettings values, else the defaults.
                BaseDomain = config[$"{ZtprOptions.SectionName}:BaseDomain"] ?? "lvh.me",
                AppHost = config[$"{ZtprOptions.SectionName}:AppHost"] ?? "localhost",
                HttpsEnabled = config.GetValue("Tls:Enabled", false),
                // Seed the tunnel limits from any legacy appsettings values, else the defaults.
                MaxTunnelHours = config.GetValue($"{ZtprOptions.SectionName}:MaxTunnelHours", 4),
                IdleTimeoutMinutes = config.GetValue($"{ZtprOptions.SectionName}:IdleTimeoutMinutes", 5),
                MaxTunnelsPerKey = config.GetValue($"{ZtprOptions.SectionName}:MaxTunnelsPerKey", 3),
                ReaperIntervalSeconds = config.GetValue($"{ZtprOptions.SectionName}:ReaperIntervalSeconds", 30),
            };
            db.AdminSettings.Add(row);
            await db.SaveChangesAsync();
        }
        return row;
    }
}

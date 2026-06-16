using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Services;

/// <summary>Reads/writes the singleton <see cref="AdminSettings"/> row, with a cached copy.</summary>
public sealed class SettingsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private AdminSettings? _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The cached settings for synchronous, hot-path access (ingress, Razor render).
    /// Warmed once at startup via <see cref="GetAsync"/>; throws if accessed before then.
    /// </summary>
    public AdminSettings Current =>
        _cache ?? throw new InvalidOperationException(
            "Settings have not been loaded yet. Call GetAsync() once during startup before using Current.");

    public async Task<AdminSettings> GetAsync()
    {
        if (_cache is not null)
            return _cache;

        await _gate.WaitAsync();
        try
        {
            if (_cache is not null)
                return _cache;

            await using var db = await dbFactory.CreateDbContextAsync();
            var row = await db.AdminSettings.FirstOrDefaultAsync(s => s.Id == AdminSettings.SingletonId);
            if (row is null)
            {
                row = new AdminSettings { Id = AdminSettings.SingletonId };
                db.AdminSettings.Add(row);
                await db.SaveChangesAsync();
            }
            _cache = row;
            return row;
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(Action<AdminSettings> mutate)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.AdminSettings.FirstOrDefaultAsync(s => s.Id == AdminSettings.SingletonId)
                  ?? new AdminSettings { Id = AdminSettings.SingletonId };
        if (db.Entry(row).State == EntityState.Detached)
            db.AdminSettings.Add(row);

        mutate(row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        _cache = row;
    }
}

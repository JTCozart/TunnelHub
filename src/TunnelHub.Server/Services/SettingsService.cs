using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;

namespace TunnelHub.Server.Services;

/// <summary>Reads/writes the singleton <see cref="AdminSettings"/> row, with a cached copy.</summary>
public sealed class SettingsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private AdminSettings? _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

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

using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;

namespace TunnelHub.Server.Tunneling;

/// <summary>
/// Coordinates the in-memory <see cref="TunnelRegistry"/> with persisted
/// <see cref="Tunnel"/> rows. Opens reserve+persist; closes update the row and
/// free the subdomain. Resolves its own DB scope so it is safe to call from the
/// long-lived socket pump, the reaper, and Blazor circuits alike.
/// </summary>
public sealed class TunnelManager(TunnelRegistry registry, IServiceScopeFactory scopeFactory, ILogger<TunnelManager> logger)
{
    public TunnelRegistry Registry => registry;

    /// <summary>Reserve the subdomain and write the Tunnel row. Returns false if the subdomain was taken.</summary>
    public async Task<bool> OpenAsync(TunnelSession session)
    {
        if (!registry.TryReserve(session.Subdomain, session))
            return false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tunnels.Add(new Tunnel
            {
                Id = session.TunnelId,
                Subdomain = session.Subdomain,
                ApiKeyId = session.ApiKeyId,
                OwnerId = session.OwnerId,
                ClientIp = session.ClientIp,
                ClientLabel = session.ClientLabel,
                StartedAt = session.StartedAt,
                ExpiresAt = session.ExpiresAt,
                LastSeenAt = session.StartedAt,
                Status = TunnelStatus.Active,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            registry.Remove(session.Subdomain);
            logger.LogError(ex, "Failed to persist tunnel {Subdomain}", session.Subdomain);
            return false;
        }
        logger.LogInformation("Tunnel opened: {Subdomain} (owner {Owner})", session.Subdomain, session.OwnerId);
        return true;
    }

    /// <summary>Close a live tunnel by subdomain and persist its closed state.</summary>
    public async Task CloseAsync(string subdomain, string reason)
    {
        var session = registry.Find(subdomain);
        if (session is not null)
            await session.CloseAsync(reason);
        registry.Remove(subdomain);
        await PersistClosedAsync(subdomain, session?.LastSeen, reason);
    }

    /// <summary>Called when the pump ends on its own (client disconnected).</summary>
    public async Task OnSessionEndedAsync(TunnelSession session, string reason)
    {
        registry.Remove(session.Subdomain);
        await PersistClosedAsync(session.Subdomain, session.LastSeen, reason);
        logger.LogInformation("Tunnel closed: {Subdomain} ({Reason})", session.Subdomain, reason);
    }

    /// <summary>Force-close every live tunnel belonging to a user (e.g. on block).</summary>
    public async Task CloseForOwnerAsync(string ownerId, string reason)
    {
        foreach (var session in registry.ForOwner(ownerId).ToList())
            await CloseAsync(session.Subdomain, reason);
    }

    private async Task PersistClosedAsync(string subdomain, DateTimeOffset? lastSeen, string reason)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Only one active tunnel can hold a subdomain at a time (registry-enforced),
            // so no ordering is needed — and SQLite can't ORDER BY DateTimeOffset anyway.
            var row = await db.Tunnels
                .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.Status == TunnelStatus.Active);
            if (row is null)
                return;
            row.Status = TunnelStatus.Closed;
            row.ClosedAt = DateTimeOffset.UtcNow;
            row.CloseReason = reason;
            if (lastSeen is not null)
                row.LastSeenAt = lastSeen.Value;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist close for {Subdomain}", subdomain);
        }
    }
}

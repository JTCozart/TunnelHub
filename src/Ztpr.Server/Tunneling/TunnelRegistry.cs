using System.Collections.Concurrent;

namespace Ztpr.Server.Tunneling;

/// <summary>
/// Thread-safe source of truth for live tunnels (subdomain -> session). Singleton.
/// Used by ingress routing, the reaper, admin disconnect, and live reports.
/// </summary>
public sealed class TunnelRegistry
{
    private readonly ConcurrentDictionary<string, TunnelSession> _bySubdomain = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised whenever a tunnel is added or removed (for live UI updates).</summary>
    public event Action? Changed;

    public bool TryReserve(string subdomain, TunnelSession session)
    {
        var added = _bySubdomain.TryAdd(subdomain, session);
        if (added) Changed?.Invoke();
        return added;
    }

    public TunnelSession? Find(string subdomain) =>
        _bySubdomain.TryGetValue(subdomain, out var s) ? s : null;

    public bool IsReserved(string subdomain) => _bySubdomain.ContainsKey(subdomain);

    public void Remove(string subdomain)
    {
        if (_bySubdomain.TryRemove(subdomain, out _))
            Changed?.Invoke();
    }

    public IReadOnlyCollection<TunnelSession> Active => _bySubdomain.Values.ToArray();

    public int CountForKey(Guid apiKeyId) =>
        _bySubdomain.Values.Count(s => s.ApiKeyId == apiKeyId);

    public IEnumerable<TunnelSession> ForOwner(string ownerId) =>
        _bySubdomain.Values.Where(s => s.OwnerId == ownerId);

    public IEnumerable<TunnelSession> ForKey(Guid apiKeyId) =>
        _bySubdomain.Values.Where(s => s.ApiKeyId == apiKeyId);
}

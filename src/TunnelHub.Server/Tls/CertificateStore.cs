using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;

namespace TunnelHub.Server.Tls;

/// <summary>
/// In-memory cache of issued certificates by host, backed by the DB. Singleton.
/// The SNI selector reads from here; the ACME service stores newly issued certs.
/// </summary>
public sealed class CertificateStore(IServiceScopeFactory scopeFactory, ILogger<CertificateStore> logger)
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>Load all persisted certs into memory once at startup.</summary>
    public async Task LoadAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.IssuedCertificates.AsNoTracking().ToListAsync();
        foreach (var row in rows)
        {
            try
            {
                _cache[row.Host] = X509CertificateLoader.LoadPkcs12(row.PfxData, null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load cached cert for {Host}", row.Host);
            }
        }
        _loaded = true;
        logger.LogInformation("Loaded {Count} cached certificate(s)", _cache.Count);
    }

    public bool IsLoaded => _loaded;

    /// <summary>Return a still-valid cert for the host, or null.</summary>
    public X509Certificate2? Get(string host)
    {
        if (_cache.TryGetValue(host, out var cert))
        {
            if (cert.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                return cert;
        }
        return null;
    }

    /// <summary>Expiry of the cached cert for a host, regardless of how soon it expires.</summary>
    public bool TryGetExpiry(string host, out DateTimeOffset notAfter)
    {
        if (_cache.TryGetValue(host, out var cert))
        {
            notAfter = cert.NotAfter.ToUniversalTime();
            return true;
        }
        notAfter = default;
        return false;
    }

    /// <summary>Drop expired certificates from the cache and DB to keep things tidy.</summary>
    public async Task PruneExpiredAsync()
    {
        var expired = _cache
            .Where(kv => kv.Value.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            .Select(kv => kv.Key)
            .ToList();
        if (expired.Count == 0)
            return;

        foreach (var host in expired)
            _cache.TryRemove(host, out _);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.IssuedCertificates.Where(c => expired.Contains(c.Host)).ExecuteDeleteAsync();
        logger.LogInformation("Pruned {Count} expired certificate(s)", expired.Count);
    }

    /// <summary>Cache + persist a freshly issued cert.</summary>
    public async Task SaveAsync(string host, X509Certificate2 cert)
    {
        _cache[host] = cert;
        var pfx = cert.Export(X509ContentType.Pkcs12);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.IssuedCertificates.FirstOrDefaultAsync(c => c.Host == host);
        if (row is null)
        {
            row = new IssuedCertificate { Host = host, PfxData = pfx };
            db.IssuedCertificates.Add(row);
        }
        else
        {
            row.PfxData = pfx;
        }
        row.NotBefore = cert.NotBefore.ToUniversalTime();
        row.NotAfter = cert.NotAfter.ToUniversalTime();
        row.IssuedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}

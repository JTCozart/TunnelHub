using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Tls;

/// <summary>
/// In-memory cache of issued certificates (backed by the DB), matched per SNI
/// host name including wildcard coverage. Singleton. A single wildcard cert
/// (<c>*.tun.example.com</c>) therefore serves every tunnel subdomain.
/// </summary>
public sealed class CertificateStore(IServiceScopeFactory scopeFactory, ILogger<CertificateStore> logger)
{
    private readonly object _lock = new();
    private List<Entry> _entries = [];

    private sealed record Entry(X509Certificate2 Cert, string[] DnsNames);

    public async Task LoadAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.IssuedCertificates.AsNoTracking().ToListAsync();

        var entries = new List<Entry>();
        foreach (var row in rows)
        {
            try
            {
                var cert = X509CertificateLoader.LoadPkcs12(row.PfxData, null);
                entries.Add(new Entry(cert, GetDnsNames(cert)));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load cached cert for {Host}", row.Host);
            }
        }
        lock (_lock) _entries = entries;
        logger.LogInformation("Loaded {Count} certificate(s) covering [{Names}]",
            entries.Count, string.Join(", ", entries.SelectMany(e => e.DnsNames).Distinct()));
    }

    /// <summary>Return a still-valid cert whose SAN covers <paramref name="host"/> (exact or wildcard), or null.</summary>
    public X509Certificate2? Match(string host)
    {
        lock (_lock)
        {
            foreach (var e in _entries)
            {
                if (e.Cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(5))
                    continue;
                if (e.DnsNames.Any(n => HostMatches(n, host)))
                    return e.Cert;
            }
        }
        return null;
    }

    /// <summary>The names + expiry of currently cached certs, for the admin status display.</summary>
    public IReadOnlyList<(string[] Names, DateTimeOffset NotAfter)> Current()
    {
        lock (_lock)
            return _entries.Select(e => (e.DnsNames, (DateTimeOffset)e.Cert.NotAfter.ToUniversalTime())).ToList();
    }

    /// <summary>Cache + persist a freshly issued certificate, keyed by its primary SAN.</summary>
    public async Task SaveAsync(X509Certificate2 cert)
    {
        var names = GetDnsNames(cert);
        var primary = names.FirstOrDefault() ?? cert.Subject;
        var pfx = cert.Export(X509ContentType.Pkcs12);

        lock (_lock)
        {
            _entries.RemoveAll(e => e.DnsNames.SequenceEqual(names));
            _entries.Add(new Entry(cert, names));
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.IssuedCertificates.FirstOrDefaultAsync(c => c.Host == primary);
        if (row is null)
        {
            row = new IssuedCertificate { Host = primary, PfxData = pfx };
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
        logger.LogInformation("Stored certificate for [{Names}], valid until {Expiry:u}",
            string.Join(", ", names), cert.NotAfter);
    }

    private static bool HostMatches(string certName, string host)
    {
        if (certName.StartsWith("*.", StringComparison.Ordinal))
        {
            // "*.tun.example.com" covers exactly one extra label: "sub.tun.example.com".
            var suffix = certName[1..]; // ".tun.example.com"
            var dot = host.IndexOf('.');
            return dot > 0 && host[dot..].Equals(suffix, StringComparison.OrdinalIgnoreCase);
        }
        return certName.Equals(host, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetDnsNames(X509Certificate2 cert)
    {
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17") // subjectAltName
            {
                var san = new X509SubjectAlternativeNameExtension(ext.RawData);
                var names = san.EnumerateDnsNames().ToArray();
                if (names.Length > 0)
                    return names;
            }
        }
        var cn = cert.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        return string.IsNullOrEmpty(cn) ? [] : [cn];
    }
}

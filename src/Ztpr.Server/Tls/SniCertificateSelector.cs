using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Ztpr.Server.Tls;

/// <summary>
/// Chooses the TLS certificate per SNI host name from the <see cref="CertificateStore"/>
/// (a wildcard cert covers every tunnel subdomain). Falls back to a self-signed
/// cert when nothing matches, so handshakes never hard-fail. Constructed before
/// the DI container is built (Kestrel needs it early), then <see cref="Attach"/>ed.
/// </summary>
public sealed class SniCertificateSelector
{
    private readonly X509Certificate2 _fallback;
    private CertificateStore? _store;
    private ILogger? _logger;

    public SniCertificateSelector() => _fallback = CreateSelfSigned("ztpr.local");

    public void Attach(CertificateStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public X509Certificate2 Select(string? hostName)
    {
        if (!string.IsNullOrEmpty(hostName) && _store is not null)
        {
            var cert = _store.Match(hostName);
            if (cert is not null)
                return cert;
            _logger?.LogDebug("No matching cert for {Host}; serving self-signed fallback", hostName);
        }
        return _fallback;
    }

    /// <summary>A throwaway self-signed cert so TLS handshakes don't fail before a real cert is issued.</summary>
    public static X509Certificate2 CreateSelfSigned(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // Re-import so the private key is persistable/exportable for Kestrel.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
    }
}

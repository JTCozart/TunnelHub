using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TunnelHub.Server.Tls;

/// <summary>
/// Chooses the TLS certificate per SNI host name. Returns a cached Let's Encrypt
/// cert when available; otherwise returns a self-signed fallback and triggers
/// background issuance so the next handshake succeeds with a real cert.
/// Constructed before the DI container is built (Kestrel needs it early), then
/// <see cref="Attach"/>ed to its services afterwards.
/// </summary>
public sealed class SniCertificateSelector
{
    private readonly X509Certificate2 _fallback;
    private CertificateStore? _store;
    private AcmeService? _acme;
    private ILogger? _logger;

    public SniCertificateSelector() => _fallback = CreateSelfSigned("tunnelhub.local");

    public void Attach(CertificateStore store, AcmeService acme, ILogger logger)
    {
        _store = store;
        _acme = acme;
        _logger = logger;
    }

    public X509Certificate2 Select(string? hostName)
    {
        if (string.IsNullOrEmpty(hostName) || _store is null)
            return _fallback;

        var cert = _store.Get(hostName);
        if (cert is not null)
            return cert;

        // No cert yet — serve fallback now, fetch a real one for next time.
        _acme?.EnsureInBackground(hostName);
        _logger?.LogDebug("No cert for {Host} yet; serving fallback", hostName);
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

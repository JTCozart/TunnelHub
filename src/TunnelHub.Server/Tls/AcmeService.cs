using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using TunnelHub.Server.Data.Entities;
using TunnelHub.Server.Services;

namespace TunnelHub.Server.Tls;

/// <summary>
/// Obtains per-host certificates from Let's Encrypt via the HTTP-01 challenge,
/// using the runtime <see cref="AdminSettings"/> for email / ToS / staging.
/// Issuance is triggered at tunnel registration and runs in the background; the
/// SNI selector serves the cert once it lands.
/// </summary>
public sealed class AcmeService(
    SettingsService settings,
    CertificateStore certificates,
    AcmeChallengeStore challenges,
    ILogger<AcmeService> logger)
{
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kick off issuance for a host without blocking the caller. Safe to call repeatedly.</summary>
    public void EnsureInBackground(string host)
    {
        if (certificates.Get(host) is not null)
            return;

        _inFlight.GetOrAdd(host, h => Task.Run(async () =>
        {
            try { await IssueAsync(h); }
            catch (Exception ex) { logger.LogWarning(ex, "Certificate issuance failed for {Host}", h); }
            finally { _inFlight.TryRemove(h, out _); }
        }));
    }

    /// <summary>Issue (or reuse) a certificate for a single host. Returns the cert or null if disabled/failed.</summary>
    public async Task<X509Certificate2?> IssueAsync(string host)
    {
        var existing = certificates.Get(host);
        if (existing is not null)
            return existing;

        var cfg = await settings.GetAsync();
        if (!cfg.AcmeEnabled || !cfg.AcmeAgreeTos || string.IsNullOrWhiteSpace(cfg.AcmeEmail))
        {
            logger.LogDebug("ACME not configured; skipping issuance for {Host}", host);
            return null;
        }

        logger.LogInformation("Requesting Let's Encrypt certificate for {Host} ({Env})",
            host, cfg.UseStaging ? "staging" : "production");

        var acme = await BuildContextAsync(cfg);
        var order = await acme.NewOrder([host]);

        var authz = (await order.Authorizations()).First();
        var httpChallenge = await authz.Http();
        challenges.Add(httpChallenge.Token, httpChallenge.KeyAuthz);
        try
        {
            await httpChallenge.Validate();
            await WaitForValidAsync(authz);

            var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var certChain = await order.Generate(new CsrInfo { CommonName = host }, privateKey);
            var pfx = certChain.ToPfx(privateKey).Build(host, string.Empty);
            var cert = X509CertificateLoader.LoadPkcs12(pfx, string.Empty);

            await certificates.SaveAsync(host, cert);
            logger.LogInformation("Issued certificate for {Host}, valid until {Expiry:u}", host, cert.NotAfter);
            return cert;
        }
        finally
        {
            challenges.Remove(httpChallenge.Token);
        }
    }

    private async Task<AcmeContext> BuildContextAsync(AdminSettings cfg)
    {
        var server = cfg.UseStaging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;

        if (!string.IsNullOrWhiteSpace(cfg.AcmeAccountKeyPem))
        {
            var acme = new AcmeContext(server, KeyFactory.FromPem(cfg.AcmeAccountKeyPem));
            await acme.Account(); // bind to the existing account
            return acme;
        }

        // First run: create the ACME account and persist its key for reuse.
        var fresh = new AcmeContext(server);
        await fresh.NewAccount(cfg.AcmeEmail!, termsOfServiceAgreed: true);
        var pem = fresh.AccountKey.ToPem();
        await settings.UpdateAsync(s => s.AcmeAccountKeyPem = pem);
        return fresh;
    }

    private static async Task WaitForValidAsync(IAuthorizationContext authz)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var resource = await authz.Resource();
            switch (resource.Status)
            {
                case AuthorizationStatus.Valid:
                    return;
                case AuthorizationStatus.Invalid:
                    throw new InvalidOperationException(
                        $"ACME authorization failed: {resource.Challenges?.FirstOrDefault()?.Error?.Detail}");
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("Timed out waiting for ACME authorization to validate.");
    }
}

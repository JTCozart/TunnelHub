using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.Extensions.Options;
using TunnelHub.Server.Configuration;
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
    IOptions<TunnelHubOptions> tunnelOptions,
    ILogger<AcmeService> logger)
{
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The long-lived hosts that should always have a current cert: the app host plus any admin-configured names.</summary>
    public async Task<IReadOnlyList<string>> GetManagedHostsAsync()
    {
        var cfg = await settings.GetAsync();
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appHost = tunnelOptions.Value.AppHost;
        // Skip non-public dev hosts.
        if (!string.IsNullOrWhiteSpace(appHost) &&
            !appHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            hosts.Add(appHost.ToLowerInvariant());
        foreach (var h in cfg.ManagedHostList())
            hosts.Add(h);
        return hosts.ToList();
    }

    /// <summary>
    /// Ensure every managed host has a current certificate, renewing any that
    /// expire within the configured window, and prune expired certs. Called at
    /// startup and on a schedule by the renewal service.
    /// </summary>
    public async Task EnsureManagedAndRenewAsync(CancellationToken ct = default)
    {
        var cfg = await settings.GetAsync();
        if (!cfg.AcmeEnabled || !cfg.AcmeAgreeTos || string.IsNullOrWhiteSpace(cfg.AcmeEmail))
            return;

        var window = TimeSpan.FromDays(Math.Max(1, cfg.RenewWithinDays));
        foreach (var host in await GetManagedHostsAsync())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hasCert = certificates.TryGetExpiry(host, out var notAfter);
                var dueForRenewal = hasCert && notAfter - DateTimeOffset.UtcNow <= window;
                if (!hasCert)
                {
                    logger.LogInformation("Provisioning certificate for managed host {Host}", host);
                    await IssueAsync(host);
                }
                else if (dueForRenewal)
                {
                    logger.LogInformation("Renewing certificate for {Host} (expires {Expiry:u})", host, notAfter);
                    await IssueAsync(host, force: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Managed certificate upkeep failed for {Host}", host);
            }
        }

        await certificates.PruneExpiredAsync();
    }

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

    /// <summary>
    /// Issue (or reuse) a certificate for a single host. Returns the cert or null
    /// if disabled/failed. Pass <paramref name="force"/> to reissue even when a
    /// valid cached cert exists (used for renewal).
    /// </summary>
    public async Task<X509Certificate2?> IssueAsync(string host, bool force = false)
    {
        if (!force)
        {
            var existing = certificates.Get(host);
            if (existing is not null)
                return existing;
        }

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

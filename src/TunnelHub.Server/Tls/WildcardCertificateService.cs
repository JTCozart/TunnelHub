using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using TunnelHub.Server.Services;

namespace TunnelHub.Server.Tls;

/// <summary>
/// Drives the two-step wildcard issuance wizard via Let's Encrypt DNS-01:
/// <list type="number">
///   <item><see cref="BeginAsync"/> creates the ACME order and returns the TXT
///   records the admin must add at their DNS provider.</item>
///   <item><see cref="CompleteAsync"/> validates those records, finalizes the
///   order, and stores the wildcard certificate.</item>
/// </list>
/// One pending order is held in memory (admin-driven, low concurrency). If the
/// process restarts mid-wizard, the admin simply starts over.
/// </summary>
public sealed class WildcardCertificateService(
    SettingsService settings,
    CertificateStore store,
    ILogger<WildcardCertificateService> logger)
{
    private Pending? _pending;

    public sealed record DnsRecord(string Name, string Type, string Value);
    public sealed record BeginResult(bool Ok, string? Error, IReadOnlyList<DnsRecord> Records);
    public sealed record CompleteResult(bool Ok, string? Error, DateTimeOffset? NotAfter, IReadOnlyList<string> Domains);

    private sealed class Pending
    {
        public required AcmeContext Acme { get; init; }
        public required IOrderContext Order { get; init; }
        public required List<IChallengeContext> Challenges { get; init; }
        public required string[] Domains { get; init; }
    }

    public bool HasPending => _pending is not null;
    public IReadOnlyList<DnsRecord>? CurrentRecords { get; private set; }
    public string[]? PendingDomains => _pending?.Domains;

    /// <summary>Create an ACME order for the given domains and return the DNS-01 TXT records to add.</summary>
    public async Task<BeginResult> BeginAsync(string[] domains)
    {
        var cfg = await settings.GetAsync();
        if (!cfg.AcmeAgreeTos || string.IsNullOrWhiteSpace(cfg.AcmeEmail))
            return new BeginResult(false, "Set a contact email and agree to the Terms of Service first.", []);
        if (domains.Length == 0)
            return new BeginResult(false, "Enter at least one domain.", []);

        try
        {
            var server = cfg.UseStaging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
            var accountKey = !string.IsNullOrWhiteSpace(cfg.AcmeAccountKeyPem)
                ? KeyFactory.FromPem(cfg.AcmeAccountKeyPem)
                : KeyFactory.NewKey(KeyAlgorithm.ES256);

            var acme = new AcmeContext(server, accountKey);
            await acme.NewAccount(cfg.AcmeEmail!, termsOfServiceAgreed: true);
            if (string.IsNullOrWhiteSpace(cfg.AcmeAccountKeyPem))
                await settings.UpdateAsync(s => s.AcmeAccountKeyPem = accountKey.ToPem());

            var order = await acme.NewOrder(domains);
            var challenges = new List<IChallengeContext>();
            var records = new List<DnsRecord>();
            foreach (var authz in await order.Authorizations())
            {
                var res = await authz.Resource();
                var identifier = res.Identifier.Value; // bare domain, e.g. tun.example.com
                var dns = await authz.Dns();
                challenges.Add(dns);
                records.Add(new DnsRecord($"_acme-challenge.{identifier}", "TXT", acme.AccountKey.DnsTxt(dns.Token)));
            }

            _pending = new Pending { Acme = acme, Order = order, Challenges = challenges, Domains = domains };
            CurrentRecords = records;
            logger.LogInformation("Started wildcard order for [{Domains}] ({Env})",
                string.Join(", ", domains), cfg.UseStaging ? "staging" : "production");
            return new BeginResult(true, null, records);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to begin wildcard order");
            return new BeginResult(false, ex.Message, []);
        }
    }

    /// <summary>Validate the DNS-01 challenges, finalize, and store the certificate.</summary>
    public async Task<CompleteResult> CompleteAsync()
    {
        var pending = _pending;
        if (pending is null)
            return new CompleteResult(false, "No pending request — start the wizard again.", null, []);

        try
        {
            foreach (var challenge in pending.Challenges)
                await challenge.Validate();

            foreach (var authz in await pending.Order.Authorizations())
                await WaitForValidAsync(authz);

            var certKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var chain = await pending.Order.Generate(new CsrInfo { CommonName = pending.Domains[0] }, certKey);
            var pfx = chain.ToPfx(certKey).Build("tunnelhub-wildcard", string.Empty);
            var cert = X509CertificateLoader.LoadPkcs12(pfx, string.Empty);

            await store.SaveAsync(cert);

            var result = new CompleteResult(true, null, cert.NotAfter.ToUniversalTime(), pending.Domains);
            _pending = null;
            CurrentRecords = null;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to complete wildcard order");
            return new CompleteResult(false, ex.Message, null, pending.Domains);
        }
    }

    public void Cancel()
    {
        _pending = null;
        CurrentRecords = null;
    }

    private static async Task WaitForValidAsync(IAuthorizationContext authz)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var resource = await authz.Resource();
            switch (resource.Status)
            {
                case AuthorizationStatus.Valid:
                    return;
                case AuthorizationStatus.Invalid:
                    throw new InvalidOperationException(
                        "DNS validation failed: " +
                        (resource.Challenges?.FirstOrDefault(c => c.Error is not null)?.Error?.Detail
                         ?? "the TXT record was not found or hasn't propagated yet."));
            }
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        throw new TimeoutException("Timed out waiting for DNS validation. Check the TXT records and DNS propagation, then retry.");
    }
}

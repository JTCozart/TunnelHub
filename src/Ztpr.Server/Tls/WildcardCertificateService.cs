using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Ztpr.Server.Services;

namespace Ztpr.Server.Tls;

/// <summary>
/// Drives wildcard issuance via Let's Encrypt DNS-01. Two modes:
/// <list type="bullet">
///   <item><b>Manual</b> — <see cref="BeginAsync"/> returns the TXT records the admin
///   adds at their DNS provider, then <see cref="CompleteAsync"/> validates and issues.</item>
///   <item><b>Automatic</b> — when Route 53 credentials are configured,
///   <see cref="IssueAutomaticAsync"/> publishes the records, waits for DNS to
///   propagate, then validates and issues — no manual steps.</item>
/// </list>
/// One pending order is held in memory (admin-driven, low concurrency). If the
/// process restarts mid-wizard, the admin simply starts over.
/// </summary>
public sealed class WildcardCertificateService(
    SettingsService settings,
    CertificateStore store,
    Route53ChallengeWriter route53,
    ILogger<WildcardCertificateService> logger)
{
    /// <summary>Seconds to wait for DNS to propagate after publishing, before validating.</summary>
    private const int PropagationWaitSeconds = 60;

    private Pending? _pending;

    public sealed record DnsRecord(string Name, string Type, string Value);
    public sealed record BeginResult(
        bool Ok, string? Error, IReadOnlyList<DnsRecord> Records, bool AutoPublished = false, string? Note = null);
    public sealed record CompleteResult(bool Ok, string? Error, DateTimeOffset? NotAfter, IReadOnlyList<string> Domains);

    /// <summary>A live progress update for the issuance UI (0–100 plus a log line).</summary>
    public sealed record Progress(int Percent, string Message);

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

    /// <summary>True when Route 53 automation is available for the current settings.</summary>
    public bool Route53Available => route53.IsConfigured(settings.Current);

    /// <summary>
    /// Fully automatic issuance: create the order, publish the TXT records to Route 53,
    /// wait for propagation, validate, and store the certificate — reporting progress
    /// throughout. Requires Route 53 to be configured.
    /// </summary>
    public async Task<CompleteResult> IssueAutomaticAsync(
        string[] domains, IProgress<Progress> report, CancellationToken ct = default)
    {
        report.Report(new(5, "Contacting Let's Encrypt and creating the certificate order…"));
        var begin = await BeginAsync(domains, report);
        if (!begin.Ok)
            return new CompleteResult(false, begin.Error, null, domains);
        if (!begin.AutoPublished)
            return new CompleteResult(false,
                begin.Note ?? "Route 53 did not accept the DNS records. Check the credentials and hosted zone.",
                null, domains);

        // Give the records time to propagate before asking Let's Encrypt to look them up.
        // Validating too early just fails, so we deliberately wait.
        for (var remaining = PropagationWaitSeconds; remaining > 0; remaining -= 5)
        {
            ct.ThrowIfCancellationRequested();
            var pct = 45 + (PropagationWaitSeconds - remaining) * 25 / PropagationWaitSeconds;
            report.Report(new(pct, $"Waiting for DNS to propagate before verifying… {remaining}s remaining"));
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, remaining)), ct);
        }

        report.Report(new(72, "Asking Let's Encrypt to validate the DNS-01 challenge…"));
        var result = await CompleteAsync(report);
        report.Report(result.Ok
            ? new(100, "Certificate issued and stored.")
            : new(100, "Issuance failed: " + result.Error));
        return result;
    }

    /// <summary>Create an ACME order for the given domains and return the DNS-01 TXT records to add.</summary>
    public async Task<BeginResult> BeginAsync(string[] domains, IProgress<Progress>? report = null)
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
            report?.Report(new(20, $"Order created with {records.Count} DNS-01 challenge record(s)."));

            // If Route 53 is configured, publish the records automatically.
            var autoPublished = false;
            string? note = null;
            if (route53.IsConfigured(cfg))
            {
                report?.Report(new(28, "Route 53 is connected — publishing TXT records automatically."));
                try
                {
                    await route53.PublishAsync(cfg,
                        records.Select(r => (r.Name, r.Value)),
                        line => report?.Report(new(35, line)));
                    autoPublished = true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Route 53 auto-publish failed; falling back to manual DNS");
                    note = $"Route 53 is configured but updating it failed ({ex.Message}). Add the records manually below.";
                    report?.Report(new(35, "Route 53 update failed: " + ex.Message));
                }
            }

            return new BeginResult(true, null, records, autoPublished, note);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to begin wildcard order");
            return new BeginResult(false, ex.Message, []);
        }
    }

    /// <summary>Validate the DNS-01 challenges, finalize, and store the certificate.</summary>
    public async Task<CompleteResult> CompleteAsync(IProgress<Progress>? report = null)
    {
        var pending = _pending;
        if (pending is null)
            return new CompleteResult(false, "No pending request — start the wizard again.", null, []);

        try
        {
            foreach (var challenge in pending.Challenges)
                await challenge.Validate();

            report?.Report(new(80, "Submitted challenges; waiting for Let's Encrypt to confirm…"));
            foreach (var authz in await pending.Order.Authorizations())
                await WaitForValidAsync(authz, report);

            report?.Report(new(92, "Validation passed. Generating and downloading the certificate…"));
            var certKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
            var chain = await pending.Order.Generate(new CsrInfo { CommonName = pending.Domains[0] }, certKey);
            var pfx = chain.ToPfx(certKey).Build("ztpr-wildcard", string.Empty);
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

    private static async Task WaitForValidAsync(IAuthorizationContext authz, IProgress<Progress>? report)
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
            if (attempt > 0 && attempt % 3 == 0)
                report?.Report(new(85, $"Still waiting for Let's Encrypt to confirm the challenge… ({resource.Status})"));
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        throw new TimeoutException("Timed out waiting for DNS validation. Check the TXT records and DNS propagation, then retry.");
    }
}

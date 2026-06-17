namespace Ztpr.Server.Data.Entities;

/// <summary>
/// Singleton settings row (always Id = 1) holding operational configuration that
/// admins edit at runtime in the web UI — domains, HTTPS, registration policy, and
/// ACME / Let's Encrypt settings — so the deployment never has to touch appsettings.
/// </summary>
public class AdminSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // --- Domains & ingress ---

    /// <summary>Wildcard base for tunnels, e.g. <c>tun.example.com</c>. Tunnels become <c>&lt;sub&gt;.{BaseDomain}</c>.</summary>
    public string BaseDomain { get; set; } = "lvh.me";

    /// <summary>The management/UI host, e.g. <c>app.example.com</c>. Requests to this host are NOT treated as ingress.</summary>
    public string AppHost { get; set; } = "localhost";

    /// <summary>
    /// When on, the server binds the HTTPS listener (443 + 80 redirect) at startup and
    /// serves the wildcard certificate. Changing this takes effect after a restart.
    /// </summary>
    public bool HttpsEnabled { get; set; }

    // --- Registration policy ---

    /// <summary>
    /// When on (the default), new users must supply a valid invite code. The first
    /// user always becomes admin without one. Admins can turn this off to allow open
    /// self-registration.
    /// </summary>
    public bool RequireInviteCode { get; set; } = true;

    // --- Tunnel limits ---

    /// <summary>Hard cap on a tunnel's lifetime, in hours. <c>0</c> means no lifetime cap.</summary>
    public int MaxTunnelHours { get; set; } = 4;

    /// <summary>Release a tunnel after this many minutes without traffic. <c>0</c> disables idle release.</summary>
    public int IdleTimeoutMinutes { get; set; } = 5;

    /// <summary>Max concurrent live tunnels per API key. <c>0</c> means unlimited.</summary>
    public int MaxTunnelsPerKey { get; set; } = 3;

    /// <summary>How often the reaper scans for expired/idle tunnels, in seconds (minimum 5).</summary>
    public int ReaperIntervalSeconds { get; set; } = 30;

    /// <summary>Whether a finite lifetime cap is in effect (<see cref="MaxTunnelHours"/> &gt; 0).</summary>
    public bool HasTunnelLifetimeCap => MaxTunnelHours > 0;

    /// <summary>Whether idle release is in effect (<see cref="IdleTimeoutMinutes"/> &gt; 0).</summary>
    public bool HasIdleTimeout => IdleTimeoutMinutes > 0;

    /// <summary>Whether a per-key concurrent-tunnel limit is in effect (<see cref="MaxTunnelsPerKey"/> &gt; 0).</summary>
    public bool HasPerKeyLimit => MaxTunnelsPerKey > 0;

    /// <summary>Hard lifetime cap as a <see cref="TimeSpan"/> (only meaningful when <see cref="HasTunnelLifetimeCap"/>).</summary>
    public TimeSpan MaxTunnelLifetime => TimeSpan.FromHours(MaxTunnelHours);

    /// <summary>Idle release threshold as a <see cref="TimeSpan"/> (only meaningful when <see cref="HasIdleTimeout"/>).</summary>
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);

    // --- ACME / Let's Encrypt ---

    /// <summary>Master switch — when off, the server serves its fallback dev cert only.</summary>
    public bool AcmeEnabled { get; set; }

    /// <summary>Contact email registered with Let's Encrypt (required by the ACME account).</summary>
    public string? AcmeEmail { get; set; }

    /// <summary>Admin must explicitly agree to the Let's Encrypt Terms of Service.</summary>
    public bool AcmeAgreeTos { get; set; }

    /// <summary>Use the staging directory (high rate limits, untrusted certs) while testing.</summary>
    public bool UseStaging { get; set; } = true;

    /// <summary>Cached PEM of the ACME account key so we reuse one registration.</summary>
    public string? AcmeAccountKeyPem { get; set; }

    /// <summary>
    /// Additional long-lived hosts to always keep a certificate for (one per line),
    /// e.g. the root/apex domain. The configured app host is managed automatically.
    /// Unlike tunnel subdomains these are renewed before expiry.
    /// </summary>
    public string? ManagedHostnames { get; set; }

    /// <summary>Renew managed certificates this many days before they expire.</summary>
    public int RenewWithinDays { get; set; } = 30;

    // --- Route 53 (automated DNS-01) ---

    /// <summary>
    /// When on, DNS-01 TXT records for wildcard issuance/renewal are written to
    /// Amazon Route 53 automatically instead of by hand.
    /// </summary>
    public bool Route53Enabled { get; set; }

    /// <summary>AWS access key ID of the IAM user scoped to manage this hosted zone.</summary>
    public string? Route53AccessKeyId { get; set; }

    /// <summary>
    /// The AWS secret access key, <strong>encrypted at rest</strong> via ASP.NET Core
    /// Data Protection. Never store or surface the plaintext.
    /// </summary>
    public string? Route53SecretAccessKeyEnc { get; set; }

    /// <summary>The Route 53 hosted zone ID (e.g. <c>Z0123456789ABCDEFGHIJ</c>) that owns the domain.</summary>
    public string? Route53HostedZoneId { get; set; }

    /// <summary>
    /// AWS region used to sign Route 53 requests. Route 53 is a global service, so
    /// <c>us-east-1</c> works for standard AWS; change it only for GovCloud/China partitions.
    /// </summary>
    public string Route53Region { get; set; } = "us-east-1";

    // --- Email (Mailjet, bring-your-own-key) ---

    /// <summary>
    /// Master switch — when off, no email is sent and the email-dependent features
    /// (verification on registration, password reset) are unavailable.
    /// </summary>
    public bool EmailEnabled { get; set; }

    /// <summary>The Mailjet API key (public part). Used as the Basic-auth username.</summary>
    public string? MailjetApiKey { get; set; }

    /// <summary>
    /// The Mailjet secret key, <strong>encrypted at rest</strong> via ASP.NET Core Data
    /// Protection (used as the Basic-auth password). Never store or surface the plaintext.
    /// </summary>
    public string? MailjetSecretKeyEnc { get; set; }

    /// <summary>The verified sender address mail is sent from, e.g. <c>noreply@example.com</c>.</summary>
    public string? EmailFromAddress { get; set; }

    /// <summary>Friendly sender name shown in recipients' inboxes, e.g. <c>Ztpr</c>.</summary>
    public string? EmailFromName { get; set; }

    /// <summary>
    /// When on (and email is configured), new self-registered users must confirm their
    /// email address before they can sign in. The first user (admin) is exempt.
    /// </summary>
    public bool RequireEmailConfirmation { get; set; }

    /// <summary>
    /// The monthly email-send allowance of the connected Mailjet plan, used purely to
    /// render a usage progress bar (Mailjet's free tier is 6,000/month). <c>0</c> hides
    /// the bar. The API does not expose the plan quota, so the admin sets it here.
    /// </summary>
    public int MailjetMonthlyEmailLimit { get; set; } = 6000;

    /// <summary>
    /// The contact-storage allowance of the connected Mailjet plan, used to render a
    /// usage progress bar. <c>0</c> means "no cap set" — the count is shown without a bar.
    /// </summary>
    public int MailjetContactLimit { get; set; }

    /// <summary>Whether email sending is configured and enabled.</summary>
    public bool HasEmail =>
        EmailEnabled
        && !string.IsNullOrWhiteSpace(MailjetApiKey)
        && !string.IsNullOrWhiteSpace(MailjetSecretKeyEnc)
        && !string.IsNullOrWhiteSpace(EmailFromAddress);

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Parsed, trimmed, de-duplicated managed hostnames (excludes blanks).</summary>
    public IEnumerable<string> ManagedHostList() =>
        (ManagedHostnames ?? string.Empty)
            .Split(['\n', '\r', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .Distinct();
}

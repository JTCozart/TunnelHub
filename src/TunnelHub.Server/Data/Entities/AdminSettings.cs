namespace TunnelHub.Server.Data.Entities;

/// <summary>
/// Singleton settings row (always Id = 1) holding ACME / Let's Encrypt
/// configuration that admins edit at runtime in the web UI.
/// </summary>
public class AdminSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

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

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Parsed, trimmed, de-duplicated managed hostnames (excludes blanks).</summary>
    public IEnumerable<string> ManagedHostList() =>
        (ManagedHostnames ?? string.Empty)
            .Split(['\n', '\r', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .Distinct();
}

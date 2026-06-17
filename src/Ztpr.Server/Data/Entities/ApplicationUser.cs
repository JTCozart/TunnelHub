using Microsoft.AspNetCore.Identity;

namespace Ztpr.Server.Data.Entities;

/// <summary>An account that can log into the web UI and own API keys.</summary>
public class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When true the user cannot log in, their API keys are rejected, and active tunnels are reaped.</summary>
    public bool IsBlocked { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    /// <summary>
    /// Number of consecutive failed backup (recovery) code attempts. Reset to 0 on a
    /// successful recovery-code sign-in or when an admin unlocks the account. When it
    /// reaches <c>MfaLockout.MaxBackupCodeFailures</c> the account is locked.
    /// </summary>
    public int BackupCodeFailedCount { get; set; }

    /// <summary>
    /// When true the user is locked out after too many failed backup-code attempts and
    /// cannot sign in until an admin unlocks them (or the admin account is reset from the
    /// server). Distinct from <see cref="IsBlocked"/>, which is a deliberate admin block.
    /// </summary>
    public bool IsLocked { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public List<ApiKey> ApiKeys { get; set; } = [];
}

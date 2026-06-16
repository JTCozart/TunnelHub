namespace TunnelHub.Server.Data.Entities;

/// <summary>
/// A credential a user registers and hands to their tunnel client. The raw key
/// is shown once at creation and never stored — only a SHA-256 hash is kept.
/// Keys carry 256 bits of entropy, so an unsalted hash is safe and lets us look
/// up a key by hash at connect time.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>User-supplied name, e.g. "laptop" or "ci-runner".</summary>
    public required string Label { get; set; }

    /// <summary>First few characters of the raw key, kept for display ("th_ab12…").</summary>
    public required string DisplayPrefix { get; set; }

    /// <summary>Hex SHA-256 of the raw key. Unique + indexed for connect-time lookup.</summary>
    public required string KeyHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? RevokedAt { get; set; }

    public List<Tunnel> Tunnels { get; set; } = [];
}

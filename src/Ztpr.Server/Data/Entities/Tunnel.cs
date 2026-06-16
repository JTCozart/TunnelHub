namespace Ztpr.Server.Data.Entities;

public enum TunnelStatus
{
    Active = 0,
    Closed = 1,
}

/// <summary>
/// A subdomain reservation tied to a live client connection. Rows persist after
/// close for historical reporting; live routing state lives in the in-memory
/// <c>TunnelRegistry</c>.
/// </summary>
public class Tunnel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The two-word label, e.g. <c>red-tiger</c> (without the base domain).</summary>
    public required string Subdomain { get; set; }

    public Guid ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }

    /// <summary>Denormalized owner for fast reporting/queries.</summary>
    public required string OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public string? ClientIp { get; set; }
    public string? ClientLabel { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Hard 4-hour cap. The reaper force-closes at/after this time.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Last time traffic flowed; drives idle release.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; set; }

    public TunnelStatus Status { get; set; } = TunnelStatus.Active;

    public string? CloseReason { get; set; }
}

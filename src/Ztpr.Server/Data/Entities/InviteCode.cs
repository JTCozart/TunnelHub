namespace Ztpr.Server.Data.Entities;

/// <summary>
/// A single-use code required to self-register (except for the very first user,
/// who becomes Admin without one).
/// </summary>
public class InviteCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The human-shareable code string. Unique.</summary>
    public required string Code { get; set; }

    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? RedeemedByUserId { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }

    public bool IsRedeemed => RedeemedByUserId is not null;

    public bool IsUsable(DateTimeOffset now) =>
        !IsRedeemed && (ExpiresAt is null || ExpiresAt > now);
}

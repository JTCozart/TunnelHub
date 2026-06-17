using System.ComponentModel.DataAnnotations.Schema;

namespace Ztpr.Server.Data.Entities;

/// <summary>Categories of security-relevant events recorded in the audit log.</summary>
public enum AuditEventType
{
    LoginSucceeded = 0,
    LoginFailed = 1,
    TunnelStarted = 2,
    ApiKeyCreated = 3,
    UserCreated = 4,
    InviteCreated = 5,
    UserBlocked = 6,
    UserUnblocked = 7,
    EmailConfirmationSent = 8,
    EmailConfirmed = 9,
    PasswordResetRequested = 10,
    PasswordResetCompleted = 11,
    UserDeleted = 12,
    ApiKeyDeleted = 13,
    InviteExpired = 14,
}

/// <summary>
/// A single security audit-log entry. Rows are retained for a few days only (see the
/// reaper) since the server has limited storage, and are surfaced in the admin log viewer.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Event time as Unix epoch milliseconds. Stored as an integer (not DateTimeOffset)
    /// because SQLite/EF can sort and range-filter integers but not DateTimeOffset.
    /// </summary>
    public long CreatedAtUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Display-only view of <see cref="CreatedAtUnixMs"/>; not mapped to a column.</summary>
    [NotMapped]
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUnixMs);

    public AuditEventType EventType { get; set; }

    /// <summary>The subject/actor's user id, when known.</summary>
    public string? UserId { get; set; }

    /// <summary>Denormalized email so entries stay readable even if the user is later deleted.</summary>
    public string? UserEmail { get; set; }

    /// <summary>Remote IP associated with the event, when it can be determined.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Free-text context (e.g. subdomain, key label, acting admin).</summary>
    public string? Detail { get; set; }
}

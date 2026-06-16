using Microsoft.AspNetCore.Identity;

namespace Ztpr.Server.Data.Entities;

/// <summary>An account that can log into the web UI and own API keys.</summary>
public class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When true the user cannot log in, their API keys are rejected, and active tunnels are reaped.</summary>
    public bool IsBlocked { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    public List<ApiKey> ApiKeys { get; set; } = [];
}

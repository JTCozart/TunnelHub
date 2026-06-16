namespace Ztpr.Server.Configuration;

/// <summary>
/// Tunnel limits bound from the "Ztpr" section of appsettings.json. Domains, HTTPS,
/// and registration policy are <em>runtime</em> settings managed in the admin UI — see
/// <see cref="Data.Entities.AdminSettings"/> — so operators rarely touch this file.
/// </summary>
public sealed class ZtprOptions
{
    public const string SectionName = "Ztpr";

    /// <summary>Hard cap on a tunnel's lifetime.</summary>
    public int MaxTunnelHours { get; set; } = 4;

    /// <summary>Release a tunnel after this many minutes without traffic.</summary>
    public int IdleTimeoutMinutes { get; set; } = 5;

    /// <summary>Max concurrent live tunnels per API key.</summary>
    public int MaxTunnelsPerKey { get; set; } = 3;

    /// <summary>How often the reaper scans for expired/idle tunnels.</summary>
    public int ReaperIntervalSeconds { get; set; } = 30;

    public TimeSpan MaxTunnelLifetime => TimeSpan.FromHours(MaxTunnelHours);
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(IdleTimeoutMinutes);
}

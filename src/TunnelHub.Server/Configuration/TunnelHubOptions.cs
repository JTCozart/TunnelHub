namespace TunnelHub.Server.Configuration;

/// <summary>Bound from the "TunnelHub" section of appsettings.json.</summary>
public sealed class TunnelHubOptions
{
    public const string SectionName = "TunnelHub";

    /// <summary>Wildcard base for tunnels, e.g. <c>tun.example.com</c>. Tunnels become <c>&lt;sub&gt;.{BaseDomain}</c>.</summary>
    public string BaseDomain { get; set; } = "lvh.me";

    /// <summary>The management/UI host, e.g. <c>app.example.com</c>. Requests to this host are NOT treated as ingress.</summary>
    public string AppHost { get; set; } = "localhost";

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

using Ztpr.Server.Tunneling;

namespace Ztpr.Server.Services;

/// <summary>
/// Periodically closes tunnels that have hit the hard lifetime cap or gone idle
/// beyond the configured threshold, freeing their subdomains. All limits are read
/// live from <see cref="SettingsService"/> each pass, so changes in the admin UI
/// take effect without a restart.
/// </summary>
public sealed class TunnelReaperService(
    TunnelManager manager,
    SettingsService settings,
    ILogger<TunnelReaperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, settings.Current.ReaperIntervalSeconds));
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                await SweepAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reaper sweep failed");
            }
        }
    }

    private async Task SweepAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var hasIdleTimeout = settings.Current.HasIdleTimeout;   // 0 minutes disables idle release
        var idleTimeout = settings.Current.IdleTimeout;
        var idleMinutes = settings.Current.IdleTimeoutMinutes;
        foreach (var session in manager.Registry.Active)
        {
            // ExpiresAt is DateTimeOffset.MaxValue when there is no lifetime cap.
            if (now >= session.ExpiresAt)
            {
                logger.LogInformation("Reaping {Subdomain}: lifetime cap reached", session.Subdomain);
                await manager.CloseAsync(session.Subdomain, "Maximum tunnel lifetime reached.");
            }
            else if (hasIdleTimeout && now - session.LastSeen >= idleTimeout)
            {
                logger.LogInformation("Reaping {Subdomain}: idle {Minutes}m", session.Subdomain, idleMinutes);
                await manager.CloseAsync(session.Subdomain, "Released after inactivity.");
            }
        }
    }
}

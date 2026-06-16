using Microsoft.Extensions.Options;
using TunnelHub.Server.Configuration;
using TunnelHub.Server.Tunneling;

namespace TunnelHub.Server.Services;

/// <summary>
/// Periodically closes tunnels that have hit the hard lifetime cap
/// (<see cref="TunnelHubOptions.MaxTunnelHours"/>) or gone idle beyond
/// <see cref="TunnelHubOptions.IdleTimeoutMinutes"/>, freeing their subdomains.
/// </summary>
public sealed class TunnelReaperService(
    TunnelManager manager,
    IOptions<TunnelHubOptions> options,
    ILogger<TunnelReaperService> logger) : BackgroundService
{
    private readonly TunnelHubOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(5, _opts.ReaperIntervalSeconds));
        using var timer = new PeriodicTimer(period);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
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
        foreach (var session in manager.Registry.Active)
        {
            if (now >= session.ExpiresAt)
            {
                logger.LogInformation("Reaping {Subdomain}: 4h cap reached", session.Subdomain);
                await manager.CloseAsync(session.Subdomain, "Maximum 4-hour lifetime reached.");
            }
            else if (now - session.LastSeen >= _opts.IdleTimeout)
            {
                logger.LogInformation("Reaping {Subdomain}: idle {Minutes}m", session.Subdomain, _opts.IdleTimeoutMinutes);
                await manager.CloseAsync(session.Subdomain, "Released after inactivity.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

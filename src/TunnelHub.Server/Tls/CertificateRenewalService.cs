namespace TunnelHub.Server.Tls;

/// <summary>
/// Keeps managed (long-lived) certificates current: provisions missing ones,
/// renews those nearing expiry, and prunes expired certs. Runs shortly after
/// startup and then twice a day. Tunnel-subdomain certs are short-lived and
/// never need renewal, so only managed hosts are handled here.
/// </summary>
public sealed class CertificateRenewalService(AcmeService acme, ILogger<CertificateRenewalService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the host a moment to finish startup (Kestrel bound, DB ready).
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await acme.EnsureManagedAndRenewAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Certificate renewal sweep failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

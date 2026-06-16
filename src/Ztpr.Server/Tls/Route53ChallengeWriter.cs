using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Ztpr.Server.Data.Entities;
using Ztpr.Server.Services;

namespace Ztpr.Server.Tls;

/// <summary>
/// Publishes ACME DNS-01 challenge TXT records to Amazon Route 53 when the admin
/// has configured credentials, so wildcard issuance/renewal needs no manual DNS edits.
/// </summary>
public sealed class Route53ChallengeWriter(SecretProtector secrets, ILogger<Route53ChallengeWriter> logger)
{
    /// <summary>True when Route 53 is enabled and fully configured with usable credentials.</summary>
    public bool IsConfigured(AdminSettings cfg) =>
        cfg.Route53Enabled
        && !string.IsNullOrWhiteSpace(cfg.Route53AccessKeyId)
        && !string.IsNullOrWhiteSpace(cfg.Route53SecretAccessKeyEnc)
        && !string.IsNullOrWhiteSpace(cfg.Route53HostedZoneId);

    /// <summary>
    /// UPSERT the challenge TXT records (grouped by name — the apex and wildcard
    /// authorizations share <c>_acme-challenge.&lt;domain&gt;</c>) into the hosted zone
    /// and wait until the change is INSYNC. Throws on failure.
    /// </summary>
    public async Task PublishAsync(
        AdminSettings cfg,
        IEnumerable<(string Name, string Value)> records,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var secret = secrets.Unprotect(cfg.Route53SecretAccessKeyEnc)
            ?? throw new InvalidOperationException("The stored Route 53 secret could not be decrypted.");

        var creds = new BasicAWSCredentials(cfg.Route53AccessKeyId, secret);
        // Route 53 is global, but the SDK still needs a region to sign requests.
        var region = string.IsNullOrWhiteSpace(cfg.Route53Region) ? "us-east-1" : cfg.Route53Region.Trim();
        var config = new AmazonRoute53Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        using var client = new AmazonRoute53Client(creds, config);

        // TXT values must be double-quoted in Route 53. Group so multiple values for
        // the same record name go into a single record set.
        var changes = records
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Change
            {
                Action = ChangeAction.UPSERT,
                ResourceRecordSet = new ResourceRecordSet
                {
                    Name = g.Key,
                    Type = RRType.TXT,
                    TTL = 60,
                    ResourceRecords = g.Select(r => new ResourceRecord { Value = $"\"{r.Value}\"" }).ToList(),
                },
            })
            .ToList();

        log?.Invoke($"Submitting {changes.Count} TXT record change(s) to Route 53 hosted zone {cfg.Route53HostedZoneId}…");
        var resp = await client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = cfg.Route53HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Changes = changes,
                Comment = "Ztpr ACME DNS-01 challenge",
            },
        }, ct);

        var changeId = resp.ChangeInfo.Id;
        logger.LogInformation("Route 53 change {ChangeId} submitted; waiting for INSYNC", changeId);
        log?.Invoke($"Change {changeId} accepted. Waiting for Route 53 to reach INSYNC…");

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var status = await client.GetChangeAsync(new GetChangeRequest { Id = changeId }, ct);
            if (status.ChangeInfo.Status == ChangeStatus.INSYNC)
            {
                log?.Invoke("Route 53 change is INSYNC across AWS name servers.");
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        throw new TimeoutException("Route 53 change did not reach INSYNC within the expected time.");
    }
}

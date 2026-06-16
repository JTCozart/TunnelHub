namespace Ztpr.Server.Services;

/// <summary>
/// Builds the least-privilege IAM policy JSON shown in the admin UI for setting up a
/// Route 53 access key. Lives outside the Razor file because the Razor parser can't
/// handle C# raw string literals.
/// </summary>
public static class Route53PolicyText
{
    /// <summary>
    /// The policy scoped to a single hosted zone. If no zone is supplied, a
    /// <c>YOUR_HOSTED_ZONE_ID</c> placeholder is left for the admin to fill in.
    /// </summary>
    public static string ForZone(string? hostedZoneId)
    {
        var zone = string.IsNullOrWhiteSpace(hostedZoneId) ? "YOUR_HOSTED_ZONE_ID" : hostedZoneId.Trim();
        return $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Sid": "EditDns01TxtRecords",
              "Effect": "Allow",
              "Action": "route53:ChangeResourceRecordSets",
              "Resource": "arn:aws:route53:::hostedzone/{{zone}}"
            },
            {
              "Sid": "ReadChangeAndZoneStatus",
              "Effect": "Allow",
              "Action": [
                "route53:GetChange",
                "route53:ListResourceRecordSets",
                "route53:GetHostedZone"
              ],
              "Resource": "*"
            }
          ]
        }
        """;
    }
}

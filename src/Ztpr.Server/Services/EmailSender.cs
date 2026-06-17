using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Services;

/// <summary>Outcome of an email send attempt.</summary>
public sealed record EmailResult(bool Succeeded, string? Error)
{
    public static EmailResult Ok { get; } = new(true, null);
    public static EmailResult Fail(string error) => new(false, error);
}

/// <summary>
/// Current Mailjet account usage for the admin usage bars. <see cref="SentThisMonth"/> and
/// <see cref="ContactCount"/> are <c>null</c> when their respective call failed.
/// </summary>
public sealed record MailjetUsage(int? SentThisMonth, int? ContactCount, string? Error);

/// <summary>Outcome of a contact-purge run: how many were deleted and why it stopped.</summary>
public sealed record PurgeResult(bool Succeeded, int Deleted, string? Error);

/// <summary>
/// Sends transactional email through Mailjet's Send API v3.1 using the operator's own
/// API key + secret (bring-your-own-key). Credentials and the sender identity come from
/// the runtime <see cref="AdminSettings"/> — the secret is decrypted on demand via
/// <see cref="SecretProtector"/> and never held in memory longer than a request.
/// </summary>
public sealed class EmailSender(
    IHttpClientFactory httpFactory,
    SettingsService settings,
    SecretProtector secrets,
    ILogger<EmailSender> logger)
{
    public const string HttpClientName = "mailjet";
    private const string SendUrl = "https://api.mailjet.com/v3.1/send";
    private const string StatsUrl = "https://api.mailjet.com/v3/REST/statcounters";
    private const string ContactUrl = "https://api.mailjet.com/v3/REST/contact";
    private const string ContactDeleteUrl = "https://api.mailjet.com/v4/contacts";

    /// <summary>True when Mailjet is enabled and fully configured.</summary>
    public bool IsConfigured => settings.Current.HasEmail;

    /// <summary>
    /// Send one HTML email. Returns the failure reason rather than throwing so callers in
    /// auth flows can degrade gracefully. <paramref name="overrides"/> lets the admin
    /// "send test" UI supply credentials that haven't been saved yet.
    /// </summary>
    public async Task<EmailResult> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        EmailCredentials? overrides = null,
        string? textBody = null,
        CancellationToken ct = default)
    {
        var cfg = settings.Current;

        var apiKey = overrides?.ApiKey ?? cfg.MailjetApiKey;
        var secretKey = overrides?.SecretKey ?? secrets.Unprotect(cfg.MailjetSecretKeyEnc);
        var fromEmail = overrides?.FromAddress ?? cfg.EmailFromAddress;
        var fromName = overrides?.FromName ?? cfg.EmailFromName;

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey)
            || string.IsNullOrWhiteSpace(fromEmail))
            return EmailResult.Fail("Email is not configured. Set the Mailjet API key, secret key, and sender address.");

        var payload = new
        {
            Messages = new[]
            {
                new
                {
                    From = new { Email = fromEmail, Name = string.IsNullOrWhiteSpace(fromName) ? fromEmail : fromName },
                    To = new[] { new { Email = toEmail } },
                    Subject = subject,
                    // Mailjet wants a plain-text alternative for deliverability; derive one if the
                    // caller didn't supply it so the message isn't HTML-only.
                    TextPart = string.IsNullOrWhiteSpace(textBody) ? StripHtml(htmlBody) : textBody,
                    HTMLPart = htmlBody,
                },
            },
        };

        try
        {
            using var client = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUrl);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return EmailResult.Ok;

            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Mailjet send failed ({Status}): {Body}", (int)response.StatusCode, body);
            var status = (int)response.StatusCode;
            var hint = status == 401
                ? "Check the API key and secret key."
                : ExtractError(body);
            return EmailResult.Fail($"Mailjet rejected the request (HTTP {status}). {hint}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mailjet send threw for {To}", toEmail);
            return EmailResult.Fail($"Could not reach Mailjet: {ex.Message}");
        }
    }

    /// <summary>
    /// Read the current account usage for the usage bars on the admin Email tab:
    /// messages sent so far this (UTC) month and the number of stored contacts. Either
    /// figure is <c>null</c> if that call failed; <see cref="MailjetUsage.Error"/> carries
    /// the first failure reason so the UI can surface it without breaking the page.
    /// </summary>
    public async Task<MailjetUsage> GetUsageAsync(CancellationToken ct = default)
    {
        var creds = ResolveCredentials();
        if (creds is null)
            return new MailjetUsage(null, null, "Email is not configured.");

        var (apiKey, secretKey) = creds.Value;
        string? error = null;

        // Messages sent this month: sum the per-day MessageSentCount counters over the
        // current calendar month. statcounters defaults to the authenticated API key.
        int? sentThisMonth = null;
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var statsQuery = $"?CounterSource=APIKey&CounterTiming=Message&CounterResolution=Day"
            + $"&FromTS={monthStart.ToUnixTimeSeconds()}&ToTS={now.ToUnixTimeSeconds()}";
        try
        {
            using var client = httpFactory.CreateClient(HttpClientName);
            using var req = AuthorizedGet(StatsUrl + statsQuery, apiKey, secretKey);
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                long total = 0;
                if (doc.RootElement.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var row in data.EnumerateArray())
                        if (row.TryGetProperty("MessageSentCount", out var c) && c.TryGetInt64(out var v))
                            total += v;
                sentThisMonth = (int)Math.Min(total, int.MaxValue);
            }
            else
            {
                error ??= $"Couldn't read send stats (HTTP {(int)resp.StatusCode}).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error ??= $"Couldn't read send stats: {ex.Message}";
        }

        // Contact count: countOnly=1 makes Mailjet return the Total without paging the rows.
        int? contactCount = null;
        try
        {
            using var client = httpFactory.CreateClient(HttpClientName);
            using var req = AuthorizedGet($"{ContactUrl}?countOnly=1&Limit=1", apiKey, secretKey);
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("Total", out var t) && t.TryGetInt32(out var n))
                    contactCount = n;
            }
            else
            {
                error ??= $"Couldn't read contact count (HTTP {(int)resp.StatusCode}).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error ??= $"Couldn't read contact count: {ex.Message}";
        }

        return new MailjetUsage(sentThisMonth, contactCount, error);
    }

    /// <summary>
    /// Permanently delete every contact stored on the connected Mailjet API key. Mailjet
    /// anonymizes each contact immediately and purges it after 30 days; this is irreversible.
    /// Contact IDs are paged from the v3 list endpoint, then each is deleted via the v4
    /// GDPR delete endpoint. <paramref name="progress"/> receives the running delete count.
    /// </summary>
    public async Task<PurgeResult> PurgeContactsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var creds = ResolveCredentials();
        if (creds is null)
            return new PurgeResult(false, 0, "Email is not configured.");

        var (apiKey, secretKey) = creds.Value;
        using var client = httpFactory.CreateClient(HttpClientName);

        var deleted = 0;
        const int pageSize = 1000;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Always page from offset 0: deleting/anonymizing contacts shrinks the list,
                // so the next batch of live IDs surfaces at the top each pass.
                long[] ids;
                using (var listReq = AuthorizedGet($"{ContactUrl}?Limit={pageSize}&Offset=0", apiKey, secretKey))
                using (var listResp = await client.SendAsync(listReq, ct))
                {
                    if (!listResp.IsSuccessStatusCode)
                        return new PurgeResult(false, deleted, $"Couldn't list contacts (HTTP {(int)listResp.StatusCode}).");

                    using var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(ct));
                    ids = doc.RootElement.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Array
                        ? data.EnumerateArray()
                            .Where(r => r.TryGetProperty("ID", out var id) && id.ValueKind == JsonValueKind.Number)
                            .Select(r => r.GetProperty("ID").GetInt64())
                            .ToArray()
                        : [];
                }

                if (ids.Length == 0)
                    break;

                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"{ContactDeleteUrl}/{id}");
                    Authorize(delReq, apiKey, secretKey);
                    using var delResp = await client.SendAsync(delReq, ct);
                    // Already-anonymized contacts can answer 404; treat that as done, not an error.
                    if (delResp.IsSuccessStatusCode || delResp.StatusCode == HttpStatusCode.NotFound)
                    {
                        deleted++;
                        progress?.Report(deleted);
                    }
                    else
                    {
                        var body = await delResp.Content.ReadAsStringAsync(ct);
                        logger.LogWarning("Mailjet contact delete failed ({Status}): {Body}", (int)delResp.StatusCode, body);
                        return new PurgeResult(false, deleted, $"Delete failed for contact {id} (HTTP {(int)delResp.StatusCode}).");
                    }
                }
            }

            return new PurgeResult(true, deleted, null);
        }
        catch (OperationCanceledException)
        {
            return new PurgeResult(false, deleted, "Cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mailjet contact purge threw");
            return new PurgeResult(false, deleted, $"Could not reach Mailjet: {ex.Message}");
        }
    }

    /// <summary>Resolve saved Mailjet credentials, or <c>null</c> when email isn't configured.</summary>
    private (string ApiKey, string SecretKey)? ResolveCredentials()
    {
        var cfg = settings.Current;
        var apiKey = cfg.MailjetApiKey;
        var secretKey = secrets.Unprotect(cfg.MailjetSecretKeyEnc);
        return string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey)
            ? null
            : (apiKey, secretKey);
    }

    private static HttpRequestMessage AuthorizedGet(string url, string apiKey, string secretKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(req, apiKey, secretKey);
        return req;
    }

    private static void Authorize(HttpRequestMessage req, string apiKey, string secretKey)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    /// <summary>
    /// Pull a human-readable message out of Mailjet's error JSON, best-effort. Mailjet returns
    /// request-level errors at the root and per-message errors under Messages[].Errors[].
    /// </summary>
    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Request-level error (e.g. malformed payload).
            if (root.TryGetProperty("ErrorMessage", out var rootMsg))
                return rootMsg.GetString() ?? "";

            // Per-message error (e.g. unvalidated sender, bad recipient).
            if (root.TryGetProperty("Messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array
                && msgs.GetArrayLength() > 0
                && msgs[0].TryGetProperty("Errors", out var errs) && errs.ValueKind == JsonValueKind.Array
                && errs.GetArrayLength() > 0)
            {
                var err = errs[0];
                var message = err.TryGetProperty("ErrorMessage", out var em) ? em.GetString() : null;
                var relatedTo = err.TryGetProperty("ErrorRelatedTo", out var rel) && rel.ValueKind == JsonValueKind.Array
                    && rel.GetArrayLength() > 0 ? rel[0].GetString() : null;
                if (!string.IsNullOrEmpty(message))
                    return relatedTo is null ? message : $"{message} (field: {relatedTo})";
            }
        }
        catch { /* not JSON — fall through */ }
        return "Check the API key, secret key, and that the sender address is a verified Mailjet sender.";
    }

    /// <summary>Crude HTML-to-text fallback for the plain-text alternative part.</summary>
    private static string StripHtml(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }
}

/// <summary>
/// Credentials that override the saved settings for a single send — used by the admin
/// "send test email" button so an operator can validate keys before saving them.
/// </summary>
public sealed record EmailCredentials(string ApiKey, string SecretKey, string FromAddress, string? FromName);

using System.Net;
using TunnelHub.Shared.Protocol;

namespace TunnelHub.Client;

/// <summary>Performs the loopback HTTP call to the user's local target and streams the result back over the tunnel.</summary>
public sealed class LocalForwarder(Uri target, FrameChannel channel)
{
    // Reuse one client; allow self-signed local targets and don't auto-redirect (pass redirects through).
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    })
    { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly HashSet<string> SkipRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection",
    };

    public async Task HandleAsync(uint requestId, RequestStart head, byte[] body, CancellationToken ct)
    {
        try
        {
            using var request = BuildRequest(head, body);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            var headers = new List<HeaderPair>();
            foreach (var h in response.Headers)
                foreach (var v in h.Value)
                    headers.Add(new HeaderPair(h.Key, v));
            foreach (var h in response.Content.Headers)
                foreach (var v in h.Value)
                    headers.Add(new HeaderPair(h.Key, v));

            await channel.SendJsonAsync(FrameType.ResponseStart, requestId, new ResponseStart
            {
                StatusCode = (int)response.StatusCode,
                Headers = headers,
            }, ct);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                await channel.SendAsync(FrameType.ResponseBodyChunk, requestId, buffer.AsMemory(0, read), ct);

            await channel.SendAsync(FrameType.ResponseEnd, requestId, ct);
        }
        catch (OperationCanceledException)
        {
            // tunnel shutting down; nothing to report
        }
        catch (Exception ex)
        {
            await TrySendFailedAsync(requestId, ex.Message, ct);
        }
    }

    private HttpRequestMessage BuildRequest(RequestStart head, byte[] body)
    {
        var uri = new Uri(target, head.PathAndQuery);
        var request = new HttpRequestMessage(new HttpMethod(head.Method), uri);

        var methodHasBody = head.HasBody && body.Length > 0;
        if (methodHasBody)
            request.Content = new ByteArrayContent(body);

        foreach (var header in head.Headers)
        {
            if (string.Equals(header.Name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                // Forward the public tunnel host so the local app builds correct
                // absolute URLs / redirects instead of pointing back at localhost.
                request.Headers.Host = header.Value;
                continue;
            }
            if (SkipRequestHeaders.Contains(header.Name))
                continue;
            if (request.Headers.TryAddWithoutValidation(header.Name, header.Value))
                continue;
            request.Content?.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }
        return request;
    }

    private async Task TrySendFailedAsync(uint requestId, string reason, CancellationToken ct)
    {
        try
        {
            await channel.SendJsonAsync(FrameType.RequestFailed, requestId, new RequestFailed { Reason = reason }, ct);
        }
        catch { /* tunnel gone */ }
    }
}

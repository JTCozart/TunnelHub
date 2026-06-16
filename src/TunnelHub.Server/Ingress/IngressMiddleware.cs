using Microsoft.Extensions.Options;
using TunnelHub.Server.Configuration;
using TunnelHub.Server.Tunneling;
using TunnelHub.Shared.Protocol;

namespace TunnelHub.Server.Ingress;

/// <summary>
/// First middleware in the pipeline. If the request host is a tunnel subdomain
/// (<c>&lt;sub&gt;.{BaseDomain}</c>) it is forwarded over the matching live
/// session and the pipeline short-circuits. Requests to the management host fall
/// through to Blazor/REST as normal.
/// </summary>
public sealed class IngressMiddleware(
    RequestDelegate next,
    TunnelRegistry registry,
    IOptions<TunnelHubOptions> options,
    ILogger<IngressMiddleware> logger)
{
    private readonly TunnelHubOptions _opts = options.Value;
    private readonly string _baseSuffix = "." + options.Value.BaseDomain;

    // Headers we must not blindly forward (hop-by-hop / framing).
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;

        // Management host (and bare base domain) → not ingress.
        if (!TryGetSubdomain(host, out var subdomain))
        {
            await next(context);
            return;
        }

        var session = registry.Find(subdomain);
        if (session is null)
        {
            await WritePlainAsync(context, StatusCodes.Status404NotFound,
                $"No active tunnel for '{subdomain}.{_opts.BaseDomain}'.");
            return;
        }

        if (context.WebSockets.IsWebSocketRequest)
        {
            // v1 forwards plain HTTP only; tunneled WebSocket upgrades are not yet supported.
            await WritePlainAsync(context, StatusCodes.Status501NotImplemented,
                "WebSocket tunneling is not supported in this version.");
            return;
        }

        await ForwardAsync(context, session, subdomain);
    }

    private bool TryGetSubdomain(string host, out string subdomain)
    {
        subdomain = "";
        if (string.Equals(host, _opts.AppHost, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!host.EndsWith(_baseSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var label = host[..^_baseSuffix.Length];
        if (label.Length == 0 || label.Contains('.'))
            return false; // bare base domain or deeper nesting → not a tunnel

        subdomain = label;
        return true;
    }

    private async Task ForwardAsync(HttpContext context, TunnelSession session, string subdomain)
    {
        var req = context.Request;
        var head = new RequestStart
        {
            Method = req.Method,
            PathAndQuery = req.Path + req.QueryString,
            HasBody = req.ContentLength is > 0 || req.Headers.ContainsKey("Transfer-Encoding"),
            Headers = req.Headers
                .Where(h => !HopByHop.Contains(h.Key))
                .SelectMany(h => h.Value.Select(v => new HeaderPair(h.Key, v ?? "")))
                .ToList(),
        };

        PendingRequest pending;
        try
        {
            pending = await session.SendRequestAsync(head, req.Body, head.HasBody, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed sending request to tunnel {Subdomain}", subdomain);
            await WritePlainAsync(context, StatusCodes.Status502BadGateway, "Tunnel client unavailable.");
            return;
        }

        using (pending)
        {
            ResponseStart responseHead;
            try
            {
                responseHead = await pending.Head.WaitAsync(context.RequestAborted);
            }
            catch (TunnelTargetException ex)
            {
                await WritePlainAsync(context, StatusCodes.Status502BadGateway,
                    $"Local target error: {ex.Message}");
                return;
            }

            context.Response.StatusCode = responseHead.StatusCode;
            foreach (var header in responseHead.Headers)
            {
                if (HopByHop.Contains(header.Name) ||
                    string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;
                context.Response.Headers.Append(header.Name, header.Value);
            }

            await foreach (var chunk in pending.Body.Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
            }
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }

    private static async Task WritePlainAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(message);
    }
}

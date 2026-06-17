using Ztpr.Server.Data.Entities;
using Ztpr.Server.Services;
using Ztpr.Shared.Protocol;

namespace Ztpr.Server.Tunneling;

/// <summary>
/// Handles the client control WebSocket at <c>/tunnel</c>: authenticate by API
/// key, allocate a short-lived subdomain, then run the multiplexing pump.
/// </summary>
public sealed class TunnelControlEndpoint(
    TunnelManager manager,
    SubdomainAllocator allocator,
    SettingsService settings,
    AuditLogService audit,
    ILogger<TunnelControlEndpoint> logger)
{
    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var channel = new FrameChannel(socket);
        var ct = context.RequestAborted;

        // 1. Expect an Auth frame.
        var first = await channel.ReceiveAsync(ct);
        if (first is null || first.Value.Type != FrameType.Auth)
        {
            await channel.SendJsonAsync(FrameType.AuthFail, 0, new AuthFail { Reason = "Expected auth frame." }, ct);
            await channel.CloseAsync("no auth");
            return;
        }

        var auth = first.Value.Json<AuthRequest>();

        // 2. Verify the API key (scoped service — owner & block check inside).
        var apiKeys = context.RequestServices.GetRequiredService<ApiKeyService>();
        var key = await apiKeys.VerifyAsync(auth.ApiKey, ct);
        if (key is null)
        {
            await channel.SendJsonAsync(FrameType.AuthFail, 0, new AuthFail { Reason = "Invalid or revoked API key." }, ct);
            await channel.CloseAsync("bad key");
            return;
        }

        // 3. Enforce the per-key concurrent-tunnel limit (0 = unlimited).
        var maxPerKey = settings.Current.MaxTunnelsPerKey;
        if (maxPerKey > 0 && manager.Registry.CountForKey(key.Id) >= maxPerKey)
        {
            await channel.SendJsonAsync(FrameType.AuthFail, 0,
                new AuthFail { Reason = $"Tunnel limit ({maxPerKey}) reached for this key." }, ct);
            await channel.CloseAsync("limit");
            return;
        }

        // 4. Allocate a unique subdomain.
        var subdomain = allocator.NextFree();
        if (subdomain is null)
        {
            await channel.SendJsonAsync(FrameType.AuthFail, 0, new AuthFail { Reason = "No subdomain available, try again." }, ct);
            await channel.CloseAsync("no subdomain");
            return;
        }

        // 0-hour cap means "no lifetime cap" — represent that as a sentinel far-future expiry.
        var expiresAt = settings.Current.HasTunnelLifetimeCap
            ? DateTimeOffset.UtcNow + settings.Current.MaxTunnelLifetime
            : DateTimeOffset.MaxValue;
        var session = new TunnelSession(
            Guid.NewGuid(), subdomain, key.OwnerId, key.Id,
            context.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(auth.ClientLabel) ? null : auth.ClientLabel.Trim(),
            expiresAt, channel);

        // 5. Reserve + persist. A race on the subdomain is retried once.
        if (!await manager.OpenAsync(session))
        {
            await channel.SendJsonAsync(FrameType.AuthFail, 0, new AuthFail { Reason = "Subdomain collision, try again." }, ct);
            await channel.CloseAsync("collision");
            return;
        }

        var fullHost = $"{subdomain}.{settings.Current.BaseDomain}";
        // TLS is served by one wildcard cert for *.BaseDomain — no per-tunnel issuance needed.

        await channel.SendJsonAsync(FrameType.AuthOk, 0, new AuthOk
        {
            Subdomain = subdomain,
            FullHost = fullHost,
            ExpiresAtUnix = expiresAt.ToUnixTimeSeconds(),
        }, ct);
        logger.LogInformation("Client {Ip} authenticated -> {Host}", session.ClientIp, fullHost);
        await audit.LogAsync(AuditEventType.TunnelStarted, key.OwnerId, key.Owner?.Email, session.ClientIp,
            detail: $"{fullHost} (key: {key.Label})", ct: ct);

        // 6. Pump until the socket closes or the tunnel is killed.
        try
        {
            await session.RunAsync(ct);
        }
        finally
        {
            await manager.OnSessionEndedAsync(session, "disconnected");
        }
    }
}

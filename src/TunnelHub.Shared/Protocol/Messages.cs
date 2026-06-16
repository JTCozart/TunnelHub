using System.Text.Json;
using System.Text.Json.Serialization;

namespace TunnelHub.Shared.Protocol;

/// <summary>JSON payloads carried inside control/metadata frames.</summary>
public static class Messages
{
    /// <summary>Shared serializer options — compact, case-insensitive.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Client authenticates and requests a tunnel reservation.</summary>
public sealed record AuthRequest
{
    /// <summary>The raw API key the user registered in the web UI.</summary>
    public required string ApiKey { get; init; }

    /// <summary>Human label shown in admin reports (e.g. the client host name).</summary>
    public string? ClientLabel { get; init; }
}

/// <summary>Server accepted the client and assigned a short-lived subdomain.</summary>
public sealed record AuthOk
{
    /// <summary>The assigned two-word subdomain label, e.g. <c>red-tiger</c>.</summary>
    public required string Subdomain { get; init; }

    /// <summary>The full host the tunnel is reachable at, e.g. <c>red-tiger.tun.example.com</c>.</summary>
    public required string FullHost { get; init; }

    /// <summary>Hard expiry (Unix seconds). The tunnel is force-closed at this time (4h cap).</summary>
    public required long ExpiresAtUnix { get; init; }
}

/// <summary>Server rejected the client.</summary>
public sealed record AuthFail
{
    public required string Reason { get; init; }
}

/// <summary>Metadata for an inbound HTTP request being forwarded to the client.</summary>
public sealed record RequestStart
{
    public required string Method { get; init; }

    /// <summary>Absolute path + query string, e.g. <c>/api/items?id=3</c>.</summary>
    public required string PathAndQuery { get; init; }

    public required List<HeaderPair> Headers { get; init; }

    /// <summary>True if request body chunks will follow.</summary>
    public bool HasBody { get; init; }
}

/// <summary>Metadata for the response coming back from the client's local target.</summary>
public sealed record ResponseStart
{
    public required int StatusCode { get; init; }

    public required List<HeaderPair> Headers { get; init; }
}

/// <summary>The client's local target could not be reached / errored for a request.</summary>
public sealed record RequestFailed
{
    public required string Reason { get; init; }
}

/// <summary>Server notifies the client a tunnel is being closed (expiry, admin disconnect, block).</summary>
public sealed record CloseNotice
{
    public required string Reason { get; init; }
}

/// <summary>A single HTTP header. Multiple values are sent as repeated pairs.</summary>
public sealed record HeaderPair(string Name, string Value);

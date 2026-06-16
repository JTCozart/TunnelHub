namespace Ztpr.Shared.Protocol;

/// <summary>
/// Frame opcodes for the Ztpr multiplexing protocol.
/// Each WebSocket binary message carries exactly one frame: a 5-byte header
/// (1 byte type + 4 byte big-endian request id) followed by an opaque payload.
/// </summary>
public enum FrameType : byte
{
    // --- Control handshake ---
    /// <summary>client -> server. Payload: JSON <see cref="Messages.AuthRequest"/>.</summary>
    Auth = 1,
    /// <summary>server -> client. Payload: JSON <see cref="Messages.AuthOk"/>.</summary>
    AuthOk = 2,
    /// <summary>server -> client. Payload: JSON <see cref="Messages.AuthFail"/>.</summary>
    AuthFail = 3,

    // --- Inbound request (server -> client), keyed by request id ---
    /// <summary>Payload: JSON <see cref="Messages.RequestStart"/>.</summary>
    RequestStart = 4,
    /// <summary>Payload: raw request body bytes.</summary>
    RequestBodyChunk = 5,
    /// <summary>No payload. End of request body.</summary>
    RequestEnd = 6,

    // --- Response (client -> server), keyed by request id ---
    /// <summary>Payload: JSON <see cref="Messages.ResponseStart"/>.</summary>
    ResponseStart = 7,
    /// <summary>Payload: raw response body bytes.</summary>
    ResponseBodyChunk = 8,
    /// <summary>No payload. End of response body.</summary>
    ResponseEnd = 9,

    // --- Liveness & teardown ---
    /// <summary>Either direction. Keep-alive.</summary>
    Ping = 10,
    /// <summary>Either direction. Reply to <see cref="Ping"/>.</summary>
    Pong = 11,
    /// <summary>server -> client. Payload: JSON <see cref="Messages.CloseNotice"/>. Tunnel is being torn down.</summary>
    Close = 12,
    /// <summary>client -> server. The local target failed for this request id. Payload: JSON <see cref="Messages.RequestFailed"/>.</summary>
    RequestFailed = 13,
}

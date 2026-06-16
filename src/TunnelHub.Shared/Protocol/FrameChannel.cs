using System.Buffers.Binary;
using System.Net.WebSockets;

namespace TunnelHub.Shared.Protocol;

/// <summary>
/// Reads and writes <see cref="Frame"/>s over a single <see cref="WebSocket"/>.
/// Each frame is sent as one binary WebSocket message: a 5-byte header
/// (1 byte <see cref="FrameType"/> + 4 byte big-endian request id) followed by
/// the payload. Writes are serialized so concurrent senders cannot interleave a
/// single message; reads are expected to come from a single pump loop.
/// </summary>
public sealed class FrameChannel(WebSocket socket, int maxFrameBytes = 8 * 1024 * 1024) : IAsyncDisposable
{
    private readonly WebSocket _socket = socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public WebSocketState State => _socket.State;

    public async Task SendAsync(FrameType type, uint requestId, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var buffer = new byte[Frame.HeaderSize + payload.Length];
        buffer[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(1, 4), requestId);
        payload.Span.CopyTo(buffer.AsSpan(Frame.HeaderSize));

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Convenience overload for a control frame with a JSON payload.</summary>
    public Task SendJsonAsync<T>(FrameType type, uint requestId, T value, CancellationToken ct = default) =>
        SendAsync(type, requestId, Frame.EncodeJson(value), ct);

    /// <summary>Convenience overload for a payload-less control frame.</summary>
    public Task SendAsync(FrameType type, uint requestId, CancellationToken ct = default) =>
        SendAsync(type, requestId, ReadOnlyMemory<byte>.Empty, ct);

    /// <summary>
    /// Reads the next complete frame, or <c>null</c> if the peer closed the socket.
    /// Assembles WebSocket message fragments into a single frame.
    /// </summary>
    public async Task<Frame?> ReceiveAsync(CancellationToken ct = default)
    {
        var chunk = new byte[64 * 1024];
        using var assembled = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(chunk, ct).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            assembled.Write(chunk, 0, result.Count);
            if (assembled.Length > maxFrameBytes)
                throw new InvalidDataException($"Frame exceeded {maxFrameBytes} bytes.");

            if (result.EndOfMessage)
                break;
        }

        var bytes = assembled.GetBuffer();
        var length = (int)assembled.Length;
        if (length < Frame.HeaderSize)
            throw new InvalidDataException("Received a frame smaller than the header.");

        var type = (FrameType)bytes[0];
        var requestId = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4));
        var payload = bytes.AsSpan(Frame.HeaderSize, length - Frame.HeaderSize).ToArray();
        return new Frame(type, requestId, payload);
    }

    public async Task CloseAsync(string reason, CancellationToken ct = default)
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct).ConfigureAwait(false);
            }
            catch (WebSocketException) { /* peer already gone */ }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync("disposing").ConfigureAwait(false);
        _socket.Dispose();
        _writeLock.Dispose();
    }
}

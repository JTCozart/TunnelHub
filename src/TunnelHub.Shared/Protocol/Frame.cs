using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace TunnelHub.Shared.Protocol;

/// <summary>A decoded protocol frame: opcode, request id, and raw payload.</summary>
public readonly struct Frame(FrameType type, uint requestId, byte[] payload)
{
    public FrameType Type { get; } = type;

    /// <summary>Correlates request/response frames. 0 for control frames (auth, ping).</summary>
    public uint RequestId { get; } = requestId;

    public byte[] Payload { get; } = payload;

    /// <summary>Deserialize the JSON payload into <typeparamref name="T"/>.</summary>
    public T Json<T>()
    {
        var value = JsonSerializer.Deserialize<T>(Payload, Messages.Json);
        return value ?? throw new InvalidDataException($"Frame {Type} had a null/invalid JSON payload.");
    }

    /// <summary>The payload decoded as UTF-8 text.</summary>
    public string Text() => Encoding.UTF8.GetString(Payload);

    public static byte[] EncodeJson<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Messages.Json);

    public const int HeaderSize = 5;
}

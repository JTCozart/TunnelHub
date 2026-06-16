using System.Collections.Concurrent;
using System.Threading.Channels;
using TunnelHub.Shared.Protocol;

namespace TunnelHub.Server.Tunneling;

/// <summary>
/// Represents one live tunnel: the client's <see cref="FrameChannel"/> plus the
/// set of in-flight HTTP requests being multiplexed over it. The receive pump
/// (<see cref="RunAsync"/>) reads response frames from the client and routes
/// them to the awaiting ingress request by id.
/// </summary>
public sealed class TunnelSession(
    Guid tunnelId,
    string subdomain,
    string ownerId,
    Guid apiKeyId,
    string? clientIp,
    string? clientLabel,
    DateTimeOffset expiresAt,
    FrameChannel channel)
{
    private readonly FrameChannel _channel = channel;
    private readonly ConcurrentDictionary<uint, PendingRequest> _pending = new();
    private int _nextRequestId;
    private long _lastSeenTicks = DateTimeOffset.UtcNow.UtcTicks;
    private readonly CancellationTokenSource _closedCts = new();

    public Guid TunnelId { get; } = tunnelId;
    public string Subdomain { get; } = subdomain;
    public string OwnerId { get; } = ownerId;
    public Guid ApiKeyId { get; } = apiKeyId;
    public string? ClientIp { get; } = clientIp;
    public string? ClientLabel { get; } = clientLabel;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public DateTimeOffset LastSeen => new(Interlocked.Read(ref _lastSeenTicks), TimeSpan.Zero);

    /// <summary>Fires when the session has fully torn down.</summary>
    public CancellationToken Closed => _closedCts.Token;

    private void Touch() => Interlocked.Exchange(ref _lastSeenTicks, DateTimeOffset.UtcNow.UtcTicks);

    public uint NextRequestId() => unchecked((uint)Interlocked.Increment(ref _nextRequestId));

    /// <summary>
    /// Forward one HTTP request to the client and return a handle that yields the
    /// response head and a stream of body chunks. Caller must <see cref="PendingRequest.Dispose"/>.
    /// </summary>
    public async Task<PendingRequest> SendRequestAsync(
        RequestStart head, Stream body, bool hasBody, CancellationToken ct)
    {
        Touch();
        var id = NextRequestId();
        var pending = new PendingRequest(id);
        _pending[id] = pending;

        await _channel.SendJsonAsync(FrameType.RequestStart, id, head, ct);

        if (hasBody)
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
                await _channel.SendAsync(FrameType.RequestBodyChunk, id, buffer.AsMemory(0, read), ct);
        }
        await _channel.SendAsync(FrameType.RequestEnd, id, ct);
        return pending;
    }

    internal void Forget(uint requestId) => _pending.TryRemove(requestId, out _);

    /// <summary>Receive pump. Runs until the socket closes or the session is killed.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closedCts.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var frame = await _channel.ReceiveAsync(linked.Token);
                if (frame is null)
                    break; // client closed

                Touch();
                var f = frame.Value;
                switch (f.Type)
                {
                    case FrameType.ResponseStart:
                        if (_pending.TryGetValue(f.RequestId, out var p1))
                            p1.HeadSource.TrySetResult(f.Json<ResponseStart>());
                        break;

                    case FrameType.ResponseBodyChunk:
                        if (_pending.TryGetValue(f.RequestId, out var p2))
                            await p2.Body.Writer.WriteAsync(f.Payload, linked.Token);
                        break;

                    case FrameType.ResponseEnd:
                        if (_pending.TryGetValue(f.RequestId, out var p3))
                            p3.Body.Writer.TryComplete();
                        break;

                    case FrameType.RequestFailed:
                        if (_pending.TryGetValue(f.RequestId, out var p4))
                        {
                            var reason = f.Json<RequestFailed>().Reason;
                            p4.HeadSource.TrySetException(new TunnelTargetException(reason));
                            p4.Body.Writer.TryComplete();
                        }
                        break;

                    case FrameType.Ping:
                        await _channel.SendAsync(FrameType.Pong, 0, linked.Token);
                        break;

                    case FrameType.Pong:
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* treat any pump error as a disconnect */ }
        finally
        {
            await TearDownAsync("connection closed");
        }
    }

    /// <summary>Tell the client we're closing, then tear down. Safe to call repeatedly.</summary>
    public async Task CloseAsync(string reason)
    {
        try
        {
            if (_channel.State == System.Net.WebSockets.WebSocketState.Open)
                await _channel.SendJsonAsync(FrameType.Close, 0, new CloseNotice { Reason = reason }, CancellationToken.None);
        }
        catch { /* best effort */ }
        await TearDownAsync(reason);
    }

    private async Task TearDownAsync(string reason)
    {
        if (!_closedCts.IsCancellationRequested)
            _closedCts.Cancel();

        foreach (var pending in _pending.Values)
        {
            pending.HeadSource.TrySetException(new TunnelTargetException(reason));
            pending.Body.Writer.TryComplete();
        }
        _pending.Clear();
        await _channel.CloseAsync(reason);
    }
}

/// <summary>The local target behind the client failed or the tunnel dropped mid-request.</summary>
public sealed class TunnelTargetException(string reason) : Exception(reason);

/// <summary>Handle for one in-flight request: awaitable head + a channel of body chunks.</summary>
public sealed class PendingRequest(uint requestId) : IDisposable
{
    public uint RequestId { get; } = requestId;

    public TaskCompletionSource<ResponseStart> HeadSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Channel<byte[]> Body { get; } = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    public Task<ResponseStart> Head => HeadSource.Task;

    public void Dispose() => Body.Writer.TryComplete();
}

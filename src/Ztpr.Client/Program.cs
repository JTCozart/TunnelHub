using System.Collections.Concurrent;
using System.Net.WebSockets;
using Ztpr.Client;
using Ztpr.Shared.Protocol;

var options = ClientOptions.Parse(args);
if (options is null)
{
    ClientOptions.PrintUsage();
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await RunAsync(options, cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nDisconnected.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static async Task RunAsync(ClientOptions options, CancellationToken ct)
{
    var wsUri = options.ControlUri();
    Console.WriteLine($"Connecting to {wsUri} …");

    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(wsUri, ct);
    var channel = new FrameChannel(socket);

    // 1. Authenticate.
    await channel.SendJsonAsync(FrameType.Auth, 0, new AuthRequest
    {
        ApiKey = options.ApiKey,
        ClientLabel = options.Label ?? Environment.MachineName,
    }, ct);

    var reply = await channel.ReceiveAsync(ct);
    if (reply is null || reply.Value.Type == FrameType.AuthFail)
    {
        var reason = reply?.Type == FrameType.AuthFail ? reply.Value.Json<AuthFail>().Reason : "connection closed";
        Console.Error.WriteLine($"Authentication failed: {reason}");
        return;
    }
    if (reply.Value.Type != FrameType.AuthOk)
    {
        Console.Error.WriteLine("Unexpected response from server.");
        return;
    }

    var ok = reply.Value.Json<AuthOk>();
    var expires = DateTimeOffset.FromUnixTimeSeconds(ok.ExpiresAtUnix).ToLocalTime();
    Console.WriteLine();
    Console.WriteLine($"  Tunnel live:  https://{ok.FullHost}");
    Console.WriteLine($"  Forwarding ->  {options.Target}");
    Console.WriteLine($"  Expires:      {expires:g}");
    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop.");

    var forwarder = new LocalForwarder(options.Target, channel);
    var bodies = new ConcurrentDictionary<uint, MemoryStream>();
    var heads = new ConcurrentDictionary<uint, RequestStart>();

    // 2. Keep-alive pings.
    _ = Task.Run(async () =>
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (channel.State == WebSocketState.Open)
                    await channel.SendAsync(FrameType.Ping, 0, ct);
            }
        }
        catch { /* shutting down */ }
    }, ct);

    // 3. Pump frames from the server.
    while (!ct.IsCancellationRequested)
    {
        var frame = await channel.ReceiveAsync(ct);
        if (frame is null)
        {
            Console.WriteLine("Server closed the connection.");
            break;
        }

        var f = frame.Value;
        switch (f.Type)
        {
            case FrameType.RequestStart:
                heads[f.RequestId] = f.Json<RequestStart>();
                bodies[f.RequestId] = new MemoryStream();
                break;

            case FrameType.RequestBodyChunk:
                if (bodies.TryGetValue(f.RequestId, out var ms))
                    ms.Write(f.Payload, 0, f.Payload.Length);
                break;

            case FrameType.RequestEnd:
                if (heads.TryRemove(f.RequestId, out var head) &&
                    bodies.TryRemove(f.RequestId, out var bodyStream))
                {
                    var id = f.RequestId;
                    var body = bodyStream.ToArray();
                    bodyStream.Dispose();
                    _ = forwarder.HandleAsync(id, head, body, ct);
                }
                break;

            case FrameType.Ping:
                await channel.SendAsync(FrameType.Pong, 0, ct);
                break;

            case FrameType.Pong:
                break;

            case FrameType.Close:
                Console.WriteLine($"Tunnel closed by server: {f.Json<CloseNotice>().Reason}");
                return;
        }
    }
}

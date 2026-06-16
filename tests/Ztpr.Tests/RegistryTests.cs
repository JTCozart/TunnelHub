using System.Net.WebSockets;
using Ztpr.Server.Tunneling;
using Ztpr.Shared.Protocol;
using Xunit;

namespace Ztpr.Tests;

public class RegistryTests
{
    private static TunnelSession MakeSession(string subdomain, string ownerId, Guid apiKeyId, DateTimeOffset expires)
    {
        // A non-communicating WebSocket is enough to construct a session for registry tests.
        var ws = WebSocket.CreateFromStream(new MemoryStream(), isServer: true, subProtocol: null, TimeSpan.FromSeconds(30));
        var channel = new FrameChannel(ws);
        return new TunnelSession(Guid.NewGuid(), subdomain, ownerId, apiKeyId, null, null, expires, channel);
    }

    [Fact]
    public void Reserve_find_and_remove()
    {
        var registry = new TunnelRegistry();
        var changes = 0;
        registry.Changed += () => changes++;

        var s = MakeSession("red-tiger", "owner1", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(4));
        Assert.True(registry.TryReserve("red-tiger", s));
        Assert.False(registry.TryReserve("red-tiger", s)); // already taken
        Assert.Same(s, registry.Find("red-tiger"));
        Assert.True(registry.IsReserved("red-tiger"));

        registry.Remove("red-tiger");
        Assert.Null(registry.Find("red-tiger"));
        Assert.Equal(2, changes); // one add, one remove
    }

    [Fact]
    public void Counts_per_key_and_owner()
    {
        var registry = new TunnelRegistry();
        var key = Guid.NewGuid();
        registry.TryReserve("a-one", MakeSession("a-one", "owner1", key, DateTimeOffset.UtcNow.AddHours(4)));
        registry.TryReserve("b-two", MakeSession("b-two", "owner1", key, DateTimeOffset.UtcNow.AddHours(4)));
        registry.TryReserve("c-three", MakeSession("c-three", "owner2", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(4)));

        Assert.Equal(2, registry.CountForKey(key));
        Assert.Equal(2, registry.ForOwner("owner1").Count());
        Assert.Equal(1, registry.ForOwner("owner2").Count());
        Assert.Equal(3, registry.Active.Count);
    }
}

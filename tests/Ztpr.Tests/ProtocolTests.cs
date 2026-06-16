using Ztpr.Server.Services;
using Ztpr.Shared.Protocol;
using Xunit;

namespace Ztpr.Tests;

public class ProtocolTests
{
    [Fact]
    public void RequestStart_round_trips_through_json()
    {
        var head = new RequestStart
        {
            Method = "POST",
            PathAndQuery = "/api/items?id=3",
            HasBody = true,
            Headers = [new HeaderPair("Content-Type", "application/json"), new HeaderPair("X-A", "1")],
        };

        var bytes = Frame.EncodeJson(head);
        var frame = new Frame(FrameType.RequestStart, 42, bytes);
        var decoded = frame.Json<RequestStart>();

        Assert.Equal(42u, frame.RequestId);
        Assert.Equal(head.Method, decoded.Method);
        Assert.Equal(head.PathAndQuery, decoded.PathAndQuery);
        Assert.True(decoded.HasBody);
        Assert.Equal(2, decoded.Headers.Count);
        Assert.Equal("application/json", decoded.Headers[0].Value);
    }

    [Fact]
    public void AuthOk_round_trips()
    {
        var ok = new AuthOk { Subdomain = "red-tiger", FullHost = "red-tiger.tun.example.com", ExpiresAtUnix = 1700000000 };
        var decoded = new Frame(FrameType.AuthOk, 0, Frame.EncodeJson(ok)).Json<AuthOk>();
        Assert.Equal("red-tiger", decoded.Subdomain);
        Assert.Equal(1700000000, decoded.ExpiresAtUnix);
    }

    [Fact]
    public void ApiKey_hash_is_deterministic_and_distinct()
    {
        Assert.Equal(ApiKeyService.Hash("ztpr_abc"), ApiKeyService.Hash("ztpr_abc"));
        Assert.NotEqual(ApiKeyService.Hash("ztpr_abc"), ApiKeyService.Hash("ztpr_def"));
        Assert.Equal(64, ApiKeyService.Hash("ztpr_abc").Length); // hex sha-256
    }
}

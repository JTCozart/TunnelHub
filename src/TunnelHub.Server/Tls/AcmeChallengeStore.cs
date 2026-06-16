using System.Collections.Concurrent;

namespace TunnelHub.Server.Tls;

/// <summary>
/// Holds in-flight HTTP-01 challenge responses (token → key authorization).
/// The challenge middleware reads from here; the ACME service writes to it.
/// Singleton.
/// </summary>
public sealed class AcmeChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _tokens = new();

    public void Add(string token, string keyAuthorization) => _tokens[token] = keyAuthorization;

    public void Remove(string token) => _tokens.TryRemove(token, out _);

    public bool TryGet(string token, out string keyAuthorization) => _tokens.TryGetValue(token, out keyAuthorization!);
}

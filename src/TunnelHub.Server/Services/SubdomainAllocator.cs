using System.Security.Cryptography;
using TunnelHub.Server.Tunneling;

namespace TunnelHub.Server.Services;

/// <summary>Generates unique random two-word subdomain labels (e.g. <c>red-tiger</c>).</summary>
public sealed class SubdomainAllocator(TunnelRegistry registry)
{
    /// <summary>
    /// Produce a label not currently reserved in the registry. Returns null if no
    /// free combination is found after many attempts (effectively never at our scale).
    /// </summary>
    public string? NextFree()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var label = Generate();
            if (!registry.IsReserved(label))
                return label;
        }
        return null;
    }

    public static string Generate()
    {
        var adj = Words.Adjectives[RandomNumberGenerator.GetInt32(Words.Adjectives.Length)];
        var noun = Words.Nouns[RandomNumberGenerator.GetInt32(Words.Nouns.Length)];
        return $"{adj}-{noun}";
    }
}

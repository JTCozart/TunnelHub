using System.Text.RegularExpressions;
using TunnelHub.Server.Services;
using TunnelHub.Server.Tunneling;
using Xunit;

namespace TunnelHub.Tests;

public class SubdomainTests
{
    [Fact]
    public void Generate_produces_two_lowercase_words()
    {
        for (var i = 0; i < 500; i++)
        {
            var label = SubdomainAllocator.Generate();
            Assert.Matches(new Regex("^[a-z]+-[a-z]+$"), label);
            var parts = label.Split('-');
            Assert.Contains(parts[0], Words.Adjectives);
            Assert.Contains(parts[1], Words.Nouns);
        }
    }

    [Fact]
    public void Wordlists_have_no_whitespace_and_are_lowercase()
    {
        foreach (var w in Words.Adjectives.Concat(Words.Nouns))
        {
            Assert.Equal(w.Trim(), w);
            Assert.Equal(w.ToLowerInvariant(), w);
            Assert.DoesNotContain(' ', w);
        }
    }

    [Fact]
    public void NextFree_returns_unreserved_label()
    {
        var registry = new TunnelRegistry();
        var allocator = new SubdomainAllocator(registry);
        var label = allocator.NextFree();
        Assert.NotNull(label);
        Assert.False(registry.IsReserved(label!));
    }
}

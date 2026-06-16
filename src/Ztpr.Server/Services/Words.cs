namespace Ztpr.Server.Services;

/// <summary>
/// Word lists for generating friendly two-word subdomains like <c>red-tiger</c>.
/// Original word lists curated for Ztpr. ~64 x ~64 gives ~4k combinations.
/// </summary>
public static class Words
{
    public static readonly string[] Adjectives =
    [
        "amber", "ancient", "azure", "bold", "brave", "brisk", "calm", "clever",
        "cosmic", "crimson", "crisp", "dapper", "dawn", "deep", "eager", "early",
        "ember", "fancy", "fleet", "fuzzy", "gentle", "giant", "golden", "happy",
        "hidden", "icy", "ivory", "jade", "jolly", "keen", "lively", "lucky",
        "lunar", "mellow", "merry", "misty", "noble", "olive", "polar", "proud",
        "quick", "quiet", "rapid", "red", "royal", "rustic", "sage", "scarlet",
        "shiny", "silent", "silver", "sleek", "solar", "spry", "stark", "sunny",
        "swift", "teal", "tidy", "vivid", "warm", "wild", "witty", "zesty",
    ];

    public static readonly string[] Nouns =
    [
        "anchor", "arrow", "badger", "beacon", "bison", "brook", "canyon", "cedar",
        "comet", "coral", "cougar", "crane", "delta", "dolphin", "dragon", "eagle",
        "ember", "falcon", "field", "finch", "fjord", "forest", "fox", "garnet",
        "glacier", "harbor", "hawk", "heron", "ibex", "island", "jaguar", "kestrel",
        "lagoon", "lantern", "leopard", "lion", "lynx", "maple", "meadow", "moose",
        "nebula", "ocelot", "orchid", "otter", "panther", "pebble", "pine", "puma",
        "quartz", "raven", "reef", "river", "robin", "sable", "salmon", "sparrow",
        "spruce", "summit", "tiger", "valley", "willow", "wolf", "wren", "zephyr",
    ];

    static Words()
    {
        // Guard against a stray leading space slipping into a label.
        for (var i = 0; i < Nouns.Length; i++)
            Nouns[i] = Nouns[i].Trim();
    }
}

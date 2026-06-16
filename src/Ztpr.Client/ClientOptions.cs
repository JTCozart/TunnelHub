namespace Ztpr.Client;

/// <summary>Parsed command-line options for the tunnel client.</summary>
public sealed class ClientOptions
{
    public required Uri Server { get; init; }
    public required string ApiKey { get; init; }
    public required Uri Target { get; init; }
    public string? Label { get; init; }

    /// <summary>Build the control WebSocket URI (http→ws, https→wss) at /tunnel.</summary>
    public Uri ControlUri()
    {
        var b = new UriBuilder(Server)
        {
            Scheme = Server.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/tunnel",
        };
        return b.Uri;
    }

    public static ClientOptions? Parse(string[] args)
    {
        string? server = null, key = null, target = null, label = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server" or "-s": server = Next(args, ref i); break;
                case "--key" or "-k": key = Next(args, ref i); break;
                case "--target" or "-t": target = Next(args, ref i); break;
                case "--label" or "-l": label = Next(args, ref i); break;
                case "--help" or "-h": return null;
            }
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(target))
            return null;

        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
        {
            Console.Error.WriteLine($"Invalid --server URL: {server}");
            return null;
        }
        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri))
        {
            Console.Error.WriteLine($"Invalid --target URL: {target}");
            return null;
        }

        return new ClientOptions { Server = serverUri, ApiKey = key, Target = targetUri, Label = label };
    }

    private static string? Next(string[] args, ref int i) => ++i < args.Length ? args[i] : null;

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Ztpr client — expose a local service through your Ztpr server.

            Usage:
              ztpr --server <url> --key <api-key> --target <local-url> [--label <name>]

            Options:
              -s, --server   Ztpr server base URL (e.g. https://app.example.com)
              -k, --key      Your API key (created in the web UI)
              -t, --target   Local URL to forward to (e.g. http://localhost:3000)
              -l, --label    Friendly name shown in the dashboard (default: machine name)
              -h, --help     Show this help

            Example:
              ztpr -s https://app.example.com -k ztpr_xxx -t http://localhost:3000
            """);
    }
}

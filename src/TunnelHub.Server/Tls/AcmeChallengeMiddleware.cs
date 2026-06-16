namespace TunnelHub.Server.Tls;

/// <summary>
/// Serves HTTP-01 challenge responses at
/// <c>/.well-known/acme-challenge/{token}</c>. Runs first in the pipeline so
/// Let's Encrypt validation requests (which target tunnel hosts) are answered by
/// us instead of being forwarded into a tunnel.
/// </summary>
public sealed class AcmeChallengeMiddleware(RequestDelegate next, AcmeChallengeStore store)
{
    private const string Prefix = "/.well-known/acme-challenge/";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is not null && path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var token = path[Prefix.Length..];
            if (store.TryGet(token, out var keyAuthorization))
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(keyAuthorization);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }
}

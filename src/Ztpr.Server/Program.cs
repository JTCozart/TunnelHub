using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Account;
using Ztpr.Server.Components;
using Ztpr.Server.Configuration;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;
using Ztpr.Server.Ingress;
using Ztpr.Server.Services;
using Ztpr.Server.Tls;
using Ztpr.Server.Tunneling;

// Offline maintenance commands (e.g. recovering a locked-out admin) short-circuit before
// the web host is built, so no ports are bound. Usage: Ztpr.Server reset-mfa <email>
if (args is [AdminMaintenance.ResetMfaCommand, var resetEmail, ..])
    return await AdminMaintenance.ResetMfaAsync(resetEmail);

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ZtprOptions>(builder.Configuration.GetSection(ZtprOptions.SectionName));

// Persist the Data Protection keyring to disk so secrets encrypted at rest (e.g. the
// Route 53 secret access key) remain decryptable across restarts and deploys.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("Ztpr");
builder.Services.AddSingleton<SecretProtector>();

// --- Data ---
var dbPath = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=ztpr.db";

// Domains and the HTTPS toggle are runtime settings (edited in the admin UI), but the
// listener must be decided before the host is built. Read the singleton settings row up
// front, migrating + seeding it on first run.
var bootSettings = await SettingsBootstrap.LoadAsync(dbPath, builder.Configuration);

// --- TLS / Let's Encrypt (one wildcard cert via DNS-01 serves all subdomains) ---
var sniSelector = new SniCertificateSelector();
builder.Services.AddSingleton(sniSelector);
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CertificateStore>();
builder.Services.AddSingleton<Route53ChallengeWriter>();
builder.Services.AddSingleton<WildcardCertificateService>();

if (bootSettings.HttpsEnabled)
{
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.ListenAnyIP(80); // plain HTTP (tunnels) + redirect for the app host
        k.ListenAnyIP(443, lo => lo.UseHttps(https =>
        {
            https.ServerCertificateSelector = (_, host) => sniSelector.Select(host);
        }));
    });
}

// Factory for long-lived Blazor circuits + a scoped instance for Identity/request services.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(dbPath));
builder.Services.AddScoped<AppDbContext>(p =>
    p.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// --- Identity (cookie auth) ---
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        // Brute-force protection: lock an account for 15 minutes after 5 failed password
        // attempts. (Failed backup-code attempts are tracked separately in AccountEndpoints.)
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddAuthorization();

// Per-IP rate limiting on the auth endpoints, to blunt password / TOTP / invite-code
// brute forcing. A legitimate user won't make 10 sign-in attempts in a minute.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AccountEndpoints.RateLimitPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// --- App services ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditLogService>();
builder.Services.AddHttpClient(EmailSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<AccountEmailService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddSingleton<TunnelRegistry>();
builder.Services.AddSingleton<TunnelManager>();
builder.Services.AddScoped<SubdomainAllocator>();
builder.Services.AddScoped<TunnelControlEndpoint>();
builder.Services.AddHostedService<TunnelReaperService>();

// --- Blazor ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

// Warm the settings cache so SettingsService.Current is available synchronously on the
// ingress hot path and during Razor rendering.
await app.Services.GetRequiredService<SettingsService>().GetAsync();

// Load cached certificates and wire the SNI selector to the store.
var certStore = app.Services.GetRequiredService<CertificateStore>();
await certStore.LoadAsync();
sniSelector.Attach(certStore, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<SniCertificateSelector>());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Redirect plain HTTP on the app host to HTTPS (tunnel subdomains stay HTTP).
if (bootSettings.HttpsEnabled)
{
    var appHost = bootSettings.AppHost;
    app.Use(async (ctx, next) =>
    {
        if (!ctx.Request.IsHttps
            && !string.IsNullOrEmpty(appHost)
            && string.Equals(ctx.Request.Host.Host, appHost, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect($"https://{appHost}{ctx.Request.Path}{ctx.Request.QueryString}", permanent: false);
            return;
        }
        await next();
    });
}

// Ingress must run before everything else: it short-circuits *.tun hosts.
app.UseMiddleware<IngressMiddleware>();

// Security response headers for the management app. Ingress short-circuits tunnel hosts
// above, so these only apply to the app/UI host — never to proxied tunnel responses
// (HSTS in particular must not leak onto the shared wildcard domain).
app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    // No includeSubDomains: scope HSTS to the exact app host so it can't pin the
    // tunnel wildcard domain in visitors' browsers.
    if (ctx.Request.IsHttps)
        headers["Strict-Transport-Security"] = "max-age=63072000";
    await next();
});

app.UseWebSockets();
app.UseRateLimiter();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Serve the client binaries from wwwroot/downloads. These are dropped in after
// `dotnet publish`, so they're not in the static-assets manifest that
// MapStaticAssets() uses — serve them directly from disk instead. ServeUnknown
// FileTypes is required because the Linux binary has no file extension.
var downloadsDir = Path.Combine(app.Environment.WebRootPath, "downloads");
Directory.CreateDirectory(downloadsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(downloadsDir),
    RequestPath = "/downloads",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
});

// Client control-plane WebSocket.
app.Map("/tunnel", async context =>
{
    var endpoint = context.RequestServices.GetRequiredService<TunnelControlEndpoint>();
    await endpoint.HandleAsync(context);
});

app.MapAccountEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

return 0;

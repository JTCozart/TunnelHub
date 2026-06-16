using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Account;
using TunnelHub.Server.Components;
using TunnelHub.Server.Configuration;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;
using TunnelHub.Server.Ingress;
using TunnelHub.Server.Services;
using TunnelHub.Server.Tls;
using TunnelHub.Server.Tunneling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TunnelHubOptions>(builder.Configuration.GetSection(TunnelHubOptions.SectionName));

// --- TLS / Let's Encrypt (per-subdomain certs via HTTP-01) ---
var tlsEnabled = builder.Configuration.GetValue("Tls:Enabled", false);
var sniSelector = new SniCertificateSelector();
builder.Services.AddSingleton(sniSelector);
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<AcmeChallengeStore>();
builder.Services.AddSingleton<CertificateStore>();
builder.Services.AddSingleton<AcmeService>();

if (tlsEnabled)
{
    var httpPort = builder.Configuration.GetValue("Tls:HttpPort", 80);
    var httpsPort = builder.Configuration.GetValue("Tls:HttpsPort", 443);
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.ListenAnyIP(httpPort); // HTTP-01 challenges + redirect
        k.ListenAnyIP(httpsPort, lo => lo.UseHttps(https =>
        {
            https.ServerCertificateSelector = (_, host) => sniSelector.Select(host);
        }));
    });
    // Keep managed-host (app/root domain) certs provisioned and renewed.
    builder.Services.AddHostedService<CertificateRenewalService>();
}

// --- Data ---
var dbPath = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=tunnelhub.db";
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
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddAuthorization();

// --- App services ---
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

// Bring up the certificate cache and wire the SNI selector to its services.
var certStore = app.Services.GetRequiredService<CertificateStore>();
await certStore.LoadAsync();
sniSelector.Attach(certStore, app.Services.GetRequiredService<AcmeService>(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<SniCertificateSelector>());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// ACME HTTP-01 challenge responses must be served before ingress forwarding.
app.UseMiddleware<AcmeChallengeMiddleware>();

// Ingress must run before everything else: it short-circuits *.tun hosts.
app.UseMiddleware<IngressMiddleware>();

app.UseWebSockets();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

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

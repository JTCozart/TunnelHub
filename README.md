# TunnelHub

Self-hosted, ngrok-style reverse-proxy tunnels with friendly two-word
subdomains, a Blazor web UI, API-key auth, invite-based registration, admin
reports, and automatic per-subdomain HTTPS via Let's Encrypt.

> **Clean-room.** The tunneling protocol and forwarding are an original
> implementation — no code from ngrok, FRP, or other tunnelers. All
> dependencies are permissively licensed (MIT / Apache-2.0).

## What it does

- Run a **client** on your machine with an API key; it opens a persistent
  WebSocket to the server and exposes a local service (e.g. `localhost:3000`).
- On connect, the server assigns a random subdomain like
  `red-tiger.tun.example.com`, **valid for up to 4 hours** and released
  automatically when the client disconnects or goes idle.
- Inbound HTTPS for `*.tun.example.com` terminates at the server (SSL offload)
  and is forwarded as plain HTTP to your local service.
- A **Blazor web UI** manages accounts, API keys, and live reports. The **first
  registered user is the admin**; everyone else needs an **invite code**.
- Admins can see all users, who holds which subdomain, and every active tunnel,
  and can **disconnect tunnels** or **block users**.

## Architecture

One ASP.NET Core process serving three concerns on the same Kestrel host:

| Concern | Where |
|---|---|
| Public ingress (`*.tun.<domain>`) | `Ingress/IngressMiddleware.cs` — routes by `Host`, forwards over the tunnel |
| Control plane (`/tunnel` WebSocket) | `Tunneling/TunnelControlEndpoint.cs` + `TunnelSession` + `TunnelRegistry` |
| Management (Blazor UI + REST) | `Components/**`, `Account/**` |

Projects:

```
src/TunnelHub.Server   ASP.NET Core (Blazor Server, EF Core/SQLite, ingress, ACME)
src/TunnelHub.Shared   Wire protocol (frames + JSON messages), shared by both ends
src/TunnelHub.Client   The downloadable cross-platform reverse-proxy client
tools/SeedKey          Dev helper to insert an API key directly (testing only)
tests/TunnelHub.Tests  xUnit tests (protocol, registry, subdomains)
```

## DNS setup (one-time, manual)

Point a wildcard and the app host at your server's static IP:

```
*.tun.example.com   A   <static-ip>
app.example.com     A   <static-ip>
```

The app **does not** talk to your DNS provider — it only tracks subdomain
reservations internally and routes by `Host` header.

Configure the domains in `appsettings.json`:

```json
"TunnelHub": {
  "BaseDomain": "tun.example.com",
  "AppHost": "app.example.com",
  "MaxTunnelHours": 4,
  "IdleTimeoutMinutes": 5,
  "MaxTunnelsPerKey": 3
}
```

## HTTPS / Let's Encrypt (wildcard via DNS-01)

TLS uses a single **wildcard certificate** (`*.tun.example.com`) from Let's
Encrypt, obtained with the **Certes** ACME client. One cert covers every random
tunnel subdomain — no per-subdomain issuance and no rate-limit worries. SSL
terminates at the server; your local service only ever sees plain HTTP.

Wildcards must be validated with a **DNS-01** challenge (a TXT record). The admin
UI provides a wizard so you don't need a DNS API token — you add the record by
hand.

Enable + issue:

1. Set `"Tls": { "Enabled": true }` so Kestrel listens on **443** (and **80** for
   the app-host HTTP→HTTPS redirect). Port 443 must be reachable. (`install.sh`
   does this with `TUNNELHUB_TLS=true`.)
2. Sign in as admin → **Settings (HTTPS)** and run the wizard:
   - Enter a contact email, agree to the Terms of Service, pick staging/production.
   - Domains default to `*.tun.example.com` and `tun.example.com` (add your app
     host if you want it on the same cert).
   - Click **Start** → the wizard shows the **TXT record(s)** to create at your
     DNS provider (name `_acme-challenge.tun.example.com`).
   - Add them, wait for propagation (`dig TXT _acme-challenge.tun.example.com +short`),
     then click **Verify & issue**.
3. The wildcard cert is stored and served on 443 for all `*.tun.example.com` hosts.

### Renewal

Let's Encrypt certs last ~90 days. Because validation is manual DNS, **re-run the
wizard before expiry** (the TXT values change each time). The Settings page shows
the current cert and days remaining. Hands-off renewal would require a DNS-API
plugin — a possible future enhancement.

> Tip: do a first run with **staging** on to confirm the DNS flow works (staging
> certs are untrusted by browsers), then switch staging off and re-run for the
> real certificate.

## Running locally (no real DNS or TLS)

`lvh.me` and `localtest.me` resolve `*.lvh.me` to `127.0.0.1`, so you can test
ingress without DNS. The default `appsettings.json` already uses
`BaseDomain=lvh.me`, `AppHost=localhost`, and `Tls:Enabled=false`.

```powershell
# 1. Start the server
dotnet run --project src/TunnelHub.Server --urls http://localhost:5000

# 2. Open http://localhost:5000 and register (first user = admin),
#    then create an API key under "API keys".

# 3. Start something locally, e.g. a site on :3000, then run the client:
dotnet run --project src/TunnelHub.Client -- `
  --server http://localhost:5000 --key <your-key> --target http://localhost:3000

# 4. The client prints a URL like http://royal-spruce.lvh.me:5000
```

## Building the downloadable clients

```powershell
pwsh deploy/publish-clients.ps1
```

This produces self-contained single-file binaries in
`src/TunnelHub.Server/wwwroot/downloads/` (`tunnelhub-win-x64.exe`,
`tunnelhub-linux-x64`) which the **Downloads** page links.

## Deploying as a Linux service

### Quick install (from a release)

Each tagged release publishes a self-contained Linux bundle (no .NET runtime
required on the box). Download it, extract, and run the installer:

```bash
# Replace OWNER/REPO with your GitHub repository
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/tunnelhub-linux-x64.tar.gz | tar xz
sudo TUNNELHUB_BASE_DOMAIN=tun.example.com TUNNELHUB_APP_HOST=app.example.com \
     ./tunnelhub/install.sh
```

`deploy/install.sh` creates the `tunnelhub` system user, installs to
`/opt/tunnelhub`, writes the systemd unit, and starts the service. Optional env
vars: `TUNNELHUB_BASE_DOMAIN`, `TUNNELHUB_APP_HOST`, `TUNNELHUB_TLS=true`. The
script also works when run from a source checkout (it publishes via the SDK).

```bash
sudo systemctl status tunnelhub
journalctl -u tunnelhub -f
```

### Manual install

```bash
dotnet publish src/TunnelHub.Server -c Release -r linux-x64 --self-contained true -o /opt/tunnelhub
sudo useradd --system --home /opt/tunnelhub tunnelhub
sudo chown -R tunnelhub:tunnelhub /opt/tunnelhub
sudo cp deploy/tunnelhub.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now tunnelhub
```

The SQLite database (`tunnelhub.db`) and issued certificates are stored in the
working directory and persisted across restarts. Operator config overrides go in
`/opt/tunnelhub/appsettings.Production.json` (preserved across upgrades).

## CI / Releases (GitHub Actions)

- **`.github/workflows/ci.yml`** — builds and runs tests on every push/PR to `main`.
- **`.github/workflows/release.yml`** — on a `v*` tag, publishes the self-contained
  Linux server bundle (`tunnelhub-linux-x64.tar.gz`, including `install.sh` and the
  client binaries) plus the standalone Windows/Linux clients, and attaches them to
  a GitHub Release. Cut a release with:

  ```bash
  git tag v0.1.0 && git push origin v0.1.0
  ```

## Client usage

```
tunnelhub --server <url> --key <api-key> --target <local-url> [--label <name>]
  -s, --server   TunnelHub server base URL (e.g. https://app.example.com)
  -k, --key      Your API key
  -t, --target   Local URL to forward to (e.g. http://localhost:3000)
  -l, --label    Friendly name shown in the dashboard
```

## Tests

```powershell
dotnet test
```

## Security notes

- API keys are stored as salted-free SHA-256 hashes (keys carry 256 bits of
  entropy) and shown only once at creation.
- Invite codes are single-use with optional expiry; the first user becomes admin.
- Blocking a user invalidates their sessions (security-stamp bump), rejects
  their API keys, and immediately reaps their active tunnels.
- A configurable per-key concurrent-tunnel limit guards against exhaustion.
- The server proxies tunnel traffic but never executes it.

## Limitations

- Tunneled **WebSocket upgrades** are not yet forwarded (plain HTTP only); the
  ingress returns `501` for upgrade requests.
- Let's Encrypt **DNS-01 / wildcard** issuance is not yet wired (per-subdomain
  HTTP-01 only) — see the rate-limit note above.

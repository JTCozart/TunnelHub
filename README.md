# Ztpr

Self-hosted reverse-proxy tunnels with friendly two-word subdomains, a Blazor
web UI, API-key auth, invite-based registration, admin reports, and automatic
per-subdomain HTTPS via Let's Encrypt.

> All dependencies are permissively licensed (MIT / Apache-2.0).

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
src/Ztpr.Server   ASP.NET Core (Blazor Server, EF Core/SQLite, ingress, ACME)
src/Ztpr.Shared   Wire protocol (frames + JSON messages), shared by both ends
src/Ztpr.Client   The downloadable cross-platform reverse-proxy client
tools/SeedKey          Dev helper to insert an API key directly (testing only)
tests/Ztpr.Tests  xUnit tests (protocol, registry, subdomains)
```

## DNS setup (one-time, manual)

Point a wildcard and the app host at your server's static IP:

```
*.tun.example.com   A   <static-ip>
app.example.com     A   <static-ip>
```

For request routing the app only tracks subdomain reservations internally and
routes by the `Host` header — it never needs your DNS provider at request time.
For **HTTPS certificate issuance** it can optionally manage the DNS-01 TXT records
for you via **Amazon Route 53** (see below); otherwise you add them by hand.

Everything operational is configured at runtime in the web UI under
**Admin → Settings** — there's no need to edit `appsettings.json`:

- **General** — base domain, app host, HTTPS, invite requirement.
- **Tunnel limits** — max session duration, idle timeout, max tunnels per key, and
  the reaper interval. **Set any limit to `0` to disable it** (no lifetime cap, no
  idle release, or unlimited tunnels per key).
- **DNS & Route 53** — optional AWS credentials for automatic DNS-01.
- **HTTPS certificate** — the wildcard issuance/renewal wizard.

The `Ztpr` section of `appsettings.json` is only read to **seed these values on
first run** (and the SQLite connection string); after that the database is
authoritative.

```json
"Ztpr": {
  "MaxTunnelHours": 4,
  "IdleTimeoutMinutes": 5,
  "MaxTunnelsPerKey": 3,
  "ReaperIntervalSeconds": 30
}
```

## HTTPS / Let's Encrypt (wildcard via DNS-01)

TLS uses a single **wildcard certificate** (`*.tun.example.com`) from Let's
Encrypt, obtained with the **Certes** ACME client. One cert covers every random
tunnel subdomain — no per-subdomain issuance and no rate-limit worries. SSL
terminates at the server; your local service only ever sees plain HTTP.

Wildcards are validated with a **DNS-01** challenge (a TXT record). Two ways to
satisfy it:

- **Automatic — Amazon Route 53.** Connect a hosted zone under
  **Admin → Settings → DNS & Route 53** with a least-privilege IAM access key (the
  UI shows a copy-paste IAM policy scoped to just that zone). When you issue or
  renew, the server writes the TXT records, waits for them to propagate, and
  validates — no manual steps, with a live progress bar. Credentials are stored
  **encrypted at rest** (ASP.NET Core Data Protection).
- **Manual — any DNS provider.** The wizard shows the TXT record(s) to add
  yourself, then you click **Verify & issue**.

Enable + issue:

1. Sign in as admin → **Admin → Settings → General**, set your base domain + app
   host, toggle **HTTPS** on, then **restart** the service so Kestrel binds **443**
   (and **80** for the app-host HTTP→HTTPS redirect). Port 443 must be reachable.
2. *(Optional, for automatic DNS)* On the **DNS & Route 53** tab, enter the AWS
   access key/secret, hosted zone ID, and region, and save.
3. On the **HTTPS certificate** tab, run the wizard:
   - Enter a contact email, agree to the Terms of Service, pick staging/production.
   - Domains default to `*.tun.example.com` and `tun.example.com` (add your app
     host if you want it on the same cert).
   - **With Route 53 connected:** click **Issue certificate automatically** and
     watch the progress log finish.
   - **Otherwise:** click **Start**, add the shown `_acme-challenge.*` TXT records,
     wait for propagation (`dig TXT _acme-challenge.tun.example.com +short`), then
     click **Verify & issue**.
4. The wildcard cert is stored and served on 443 for all `*.tun.example.com` hosts.

### Renewal

Let's Encrypt certs last ~90 days, so **re-run the wizard before expiry** (the TXT
values change each time). The Settings page shows the current cert and days
remaining. With **Route 53 connected this is one click** with no manual DNS;
otherwise add the new TXT records by hand.

> Tip: do a first run with **staging** on to confirm the DNS flow works (staging
> certs are untrusted by browsers), then switch staging off and re-run for the
> real certificate.

## Running locally (no real DNS or TLS)

`lvh.me` and `localtest.me` resolve `*.lvh.me` to `127.0.0.1`, so you can test
ingress without DNS. On first run the settings default to `BaseDomain=lvh.me`,
`AppHost=localhost`, and HTTPS off — no configuration needed.

```powershell
# 1. Start the server
dotnet run --project src/Ztpr.Server --urls http://localhost:5000

# 2. Open http://localhost:5000 and register (first user = admin),
#    then create an API key under "API keys".

# 3. Start something locally, e.g. a site on :3000, then run the client:
dotnet run --project src/Ztpr.Client -- `
  --server http://localhost:5000 --key <your-key> --target http://localhost:3000

# 4. The client prints a URL like http://royal-spruce.lvh.me:5000
```

## Building the downloadable clients

```powershell
pwsh deploy/publish-clients.ps1
```

This produces self-contained single-file binaries in
`src/Ztpr.Server/wwwroot/downloads/` (`ztpr-win-x64.exe`,
`ztpr-linux-x64`) which the **Downloads** page links.

## Deploying as a Linux service

### Build a release bundle locally (no CI)

To deploy without GitHub Actions, build the same self-contained bundle on your own
machine:

```powershell
pwsh deploy/build-release.ps1
```

This publishes the server (`linux-x64`, self-contained) plus both clients, bundles
`install.sh` / `update.sh` / `ztpr.service`, and writes `ztpr-linux-x64.tar.gz`.
Copy it to the server and install:

```bash
scp ztpr-linux-x64.tar.gz user@host:~
ssh user@host
tar xzf ztpr-linux-x64.tar.gz && cd ztpr
chmod +x install.sh update.sh && sudo ./install.sh
```

> `-Version` and `-Repo` are optional overrides (they default to `git describe` and
> the `origin` remote). `chmod +x` is needed once because tarballs created on
> Windows don't carry the Unix executable bit.

### Quick install (from a release)

Each tagged release publishes a self-contained Linux bundle (no .NET runtime
required on the box). Download it, extract, and run the installer:

```bash
# Replace OWNER/REPO with your GitHub repository
curl -fsSL https://github.com/OWNER/REPO/releases/latest/download/ztpr-linux-x64.tar.gz | tar xz
sudo ./ztpr/install.sh
```

`deploy/install.sh` creates the `ztpr` system user, installs to `/opt/ztpr`,
writes the systemd unit, and starts the service. Domains, HTTPS, and the invite
requirement are then configured in **Admin → Settings** after the first user
signs in — no config file to edit. The script also works when run from a source
checkout (it publishes via the SDK).

```bash
sudo systemctl status ztpr
journalctl -u ztpr -f
```

### Manual install

```bash
dotnet publish src/Ztpr.Server -c Release -r linux-x64 --self-contained true -o /opt/ztpr
sudo useradd --system --home /opt/ztpr ztpr
sudo chown -R ztpr:ztpr /opt/ztpr
sudo cp deploy/ztpr.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now ztpr
```

The SQLite database (`ztpr.db`) and issued certificates are stored in the
working directory and persisted across restarts. Operator config overrides go in
`/opt/ztpr/appsettings.Production.json` (preserved across upgrades).

## CI / Releases (GitHub Actions)

- **`.github/workflows/ci.yml`** — builds and runs tests on every push/PR to `main`.
- **`.github/workflows/release.yml`** — on a `v*` tag, publishes the self-contained
  Linux server bundle (`ztpr-linux-x64.tar.gz`, including `install.sh` and the
  client binaries) plus the standalone Windows/Linux clients, and attaches them to
  a GitHub Release. Cut a release with:

  ```bash
  git tag v0.1.0 && git push origin v0.1.0
  ```

## Client usage

```
ztpr --server <url> --key <api-key> --target <local-url> [--label <name>]
  -s, --server   Ztpr server base URL (e.g. https://app.example.com)
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

### Trust model — the operator can see tunnel traffic

TLS is **terminated at the server**: it decrypts inbound HTTPS with the wildcard
certificate and forwards **plain HTTP** over the tunnel to your client. That means
the server operator (anyone with access to the host) can, in principle, observe the
**decrypted request/response contents** of every tunnel. Treat the relay operator as
**trusted**.

There is **no end-to-end encryption** that hides traffic from the operator. Achieving
that would require TLS to terminate on the *client* instead (SNI passthrough, with the
client holding the certificate/key) — a different mode that is not implemented. If you
need confidentiality from the host, run the relay on infrastructure you control, and
prefer transport that is already encrypted end-to-end at the application layer.

## Limitations

- Tunneled **WebSocket upgrades** are not yet forwarded (plain HTTP only); the
  ingress returns `501` for upgrade requests.
- Certificate **renewal is admin-triggered** (re-run the wizard before expiry).
  With Route 53 this is one click, but there is no unattended background renewal yet.

## License

Ztpr is licensed under the **GNU General Public License v3.0 or later**. See
[`LICENSE`](LICENSE) for the full text and [`NOTICE`](NOTICE) for copyright and
third-party attributions.

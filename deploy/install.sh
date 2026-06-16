#!/usr/bin/env bash
#
# Ztpr installer — installs the server as a systemd service.
#
# Two modes (auto-detected):
#   * Release bundle: run from an extracted release tarball that contains the
#     published Ztpr.Server binary next to this script. No .NET needed.
#   * Source checkout: run from deploy/install.sh in the repo. Requires the
#     .NET SDK; the server is published self-contained automatically.
#
# Usage:
#   sudo ./install.sh
#
# Domains, HTTPS, and the registration policy are configured at runtime in the
# admin web UI (Admin → Settings) after the first user signs in — there is no
# appsettings file to edit.
#
set -euo pipefail

APP_USER=ztpr
INSTALL_DIR=/opt/ztpr
SERVICE_FILE=/etc/systemd/system/ztpr.service
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RID=linux-x64

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
err() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || err "Please run as root (sudo ./install.sh)."

# --- Locate the published server ---
PUBLISH_DIR=""
CLEANUP_DIR=""
if [ -f "$SCRIPT_DIR/Ztpr.Server" ]; then
    # Detect by existence, not the execute bit: tarballs created on Windows don't
    # carry the Unix exec bit. We chmod +x the binary below regardless.
    PUBLISH_DIR="$SCRIPT_DIR"
    log "Using bundled server in $PUBLISH_DIR"
elif [ -f "$SCRIPT_DIR/../src/Ztpr.Server/Ztpr.Server.csproj" ]; then
    command -v dotnet >/dev/null 2>&1 || err "The .NET SDK is required to build from source. Install it or use a release bundle."
    REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
    PUBLISH_DIR="$(mktemp -d)"
    CLEANUP_DIR="$PUBLISH_DIR"
    log "Publishing self-contained server ($RID)…"
    dotnet publish "$REPO_ROOT/src/Ztpr.Server" -c Release -r "$RID" \
        --self-contained true -o "$PUBLISH_DIR" >/dev/null
else
    err "Could not find Ztpr.Server next to this script, or a source checkout above it."
fi

# --- Service account ---
if ! id "$APP_USER" >/dev/null 2>&1; then
    log "Creating system user '$APP_USER'…"
    useradd --system --home "$INSTALL_DIR" --shell /usr/sbin/nologin "$APP_USER"
fi

# --- Install files (preserve DB + existing prod config) ---
log "Installing to $INSTALL_DIR…"
systemctl stop ztpr 2>/dev/null || true
mkdir -p "$INSTALL_DIR"
# Copy app files. Operator config lives in appsettings.Production.json (preserved),
# along with the database and ACME state. The base appsettings.json ships with the
# bundle and is updated on upgrade. The publish output contains neither the prod
# config nor the DB, so the cp fallback preserves them too.
if command -v rsync >/dev/null 2>&1; then
    rsync -a --exclude 'appsettings.Production.json' --exclude 'ztpr.db*' --exclude 'letsencrypt/' \
        "$PUBLISH_DIR"/ "$INSTALL_DIR"/
else
    cp -r "$PUBLISH_DIR"/. "$INSTALL_DIR"/
fi
chmod +x "$INSTALL_DIR/Ztpr.Server"

chown -R "$APP_USER":"$APP_USER" "$INSTALL_DIR"

# --- systemd unit ---
log "Installing systemd service…"
if [ -f "$SCRIPT_DIR/ztpr.service" ]; then
    install -m 0644 "$SCRIPT_DIR/ztpr.service" "$SERVICE_FILE"
else
    cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=Ztpr server (reverse-proxy tunnels)
After=network.target

[Service]
Type=simple
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/Ztpr.Server
Restart=always
RestartSec=5
User=$APP_USER
Group=$APP_USER
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:80
AmbientCapabilities=CAP_NET_BIND_SERVICE
ReadWritePaths=$INSTALL_DIR
SyslogIdentifier=ztpr

[Install]
WantedBy=multi-user.target
EOF
fi

systemctl daemon-reload
systemctl enable ztpr >/dev/null 2>&1 || true
systemctl restart ztpr

[ -n "$CLEANUP_DIR" ] && rm -rf "$CLEANUP_DIR"

log "Installed. Service status:"
systemctl --no-pager --full status ztpr | head -n 8 || true
cat <<'EOF'

Next steps:
  * Point DNS at this host:  *.tun.<domain> A <ip>  and  app.<domain> A <ip>
  * Open http://<this-host>/ and register — the first user becomes admin.
  * In Admin → Settings, set your base domain + app host, then enable HTTPS
    and run the certificate wizard. Restart after enabling HTTPS:
      sudo systemctl restart ztpr
  * Logs:    journalctl -u ztpr -f
EOF

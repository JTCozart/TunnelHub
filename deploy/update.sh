#!/usr/bin/env bash
#
# Ztpr updater — checks GitHub for the latest release and installs it,
# preserving your appsettings, database, and certificates.
#
# Usage:
#   sudo ./update.sh            # update if a newer release exists
#   sudo ./update.sh --check    # report current/latest, install nothing
#   sudo ./update.sh --force    # reinstall the latest even if versions match
#
# Configure the source repo (defaults to the value baked in at release time):
#   ZTPR_REPO=owner/repo sudo ./update.sh
#
set -euo pipefail

INSTALL_DIR=/opt/ztpr
ASSET=ztpr-linux-x64.tar.gz
REPO="${ZTPR_REPO:-OWNER/REPO}"

log() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
err() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

mode="update"
case "${1:-}" in
    --check) mode="check" ;;
    --force) mode="force" ;;
    "") ;;
    *) err "Unknown option: $1" ;;
esac

command -v curl >/dev/null 2>&1 || err "curl is required."
command -v tar  >/dev/null 2>&1 || err "tar is required."
[ "$REPO" != "OWNER/REPO" ] || err "Set ZTPR_REPO=owner/repo (or bake it into the release)."

current="$(cat "$INSTALL_DIR/VERSION" 2>/dev/null || echo none)"
log "Installed version: $current"

latest="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
    | grep -oE '"tag_name"[[:space:]]*:[[:space:]]*"[^"]+"' \
    | head -n1 | sed -E 's/.*"([^"]+)"$/\1/')"
[ -n "$latest" ] || err "Could not determine the latest release from GitHub."
log "Latest release:    $latest"

if [ "$mode" = "check" ]; then
    [ "$current" = "$latest" ] && log "Up to date." || log "Update available: $current -> $latest"
    exit 0
fi

if [ "$current" = "$latest" ] && [ "$mode" != "force" ]; then
    log "Already on the latest version. Use --force to reinstall."
    exit 0
fi

[ "$(id -u)" -eq 0 ] || err "Installing requires root (sudo ./update.sh)."

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

log "Downloading $latest…"
curl -fsSL "https://github.com/$REPO/releases/download/$latest/$ASSET" -o "$tmp/$ASSET"
tar -C "$tmp" -xzf "$tmp/$ASSET"

[ -x "$tmp/ztpr/install.sh" ] || err "Downloaded bundle is missing install.sh."

log "Installing $latest (config, database, and certs are preserved)…"
# install.sh stops the service, copies new files without touching appsettings*/db,
# reinstalls the unit, and restarts.
"$tmp/ztpr/install.sh"

log "Updated to $latest."

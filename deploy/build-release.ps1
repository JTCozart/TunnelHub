# Builds a complete, deployable Ztpr release bundle locally — the same artifact
# the GitHub Actions release workflow produces, so you can deploy without CI.
#
#   pwsh deploy/build-release.ps1
#   pwsh deploy/build-release.ps1 -Version v1.2.3 -Repo JTCozart/TunnelHub
#
# Output:
#   out/ztpr/                  the published server + clients + installer scripts
#   ztpr-linux-x64.tar.gz      that folder, tarred for scp to the server
#
# Deploy to a server:
#   scp ztpr-linux-x64.tar.gz user@host:~
#   ssh user@host
#   tar xzf ztpr-linux-x64.tar.gz && cd ztpr
#   chmod +x install.sh update.sh && sudo ./install.sh
#
[CmdletBinding()]
param(
    # Version stamped into VERSION (used by update.sh). Defaults to `git describe`.
    [string]$Version,
    # owner/repo baked into update.sh so it can self-update from GitHub releases.
    [string]$Repo
)

$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $PSScriptRoot
$out       = Join-Path $root "out"
$app       = Join-Path $out "ztpr"
$downloads = Join-Path $app "wwwroot/downloads"
$server    = Join-Path $root "src/Ztpr.Server"
$client    = Join-Path $root "src/Ztpr.Client"

# --- Resolve version + repo ---------------------------------------------------
if (-not $Version) {
    $Version = (git -C $root describe --tags --always 2>$null)
    if (-not $Version) { $Version = "local-$(Get-Date -Format yyyyMMddHHmmss)" }
}
if (-not $Repo) {
    $url = (git -C $root remote get-url origin 2>$null)
    if ($url -match '[:/]([^/:]+/[^/]+?)(?:\.git)?$') { $Repo = $Matches[1] }
}
Write-Host "Building Ztpr $Version" -ForegroundColor Cyan
if ($Repo) { Write-Host "Self-update repo: $Repo" } else { Write-Host "No repo detected — update.sh will need ZTPR_REPO set." -ForegroundColor Yellow }

# --- Clean --------------------------------------------------------------------
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $downloads | Out-Null

# --- Publish the server (self-contained linux-x64) ----------------------------
Write-Host "`nPublishing server (linux-x64, self-contained)…" -ForegroundColor Cyan
dotnet publish $server -c Release -r linux-x64 --self-contained true -o $app
if ($LASTEXITCODE) { throw "Server publish failed." }

# --- Publish the clients (single-file, both platforms) ------------------------
$clients = @(
    @{ Rid = "linux-x64"; Built = "Ztpr.Client";     Out = "ztpr-linux-x64" }
    @{ Rid = "win-x64";   Built = "Ztpr.Client.exe"; Out = "ztpr-win-x64.exe" }
)
foreach ($c in $clients) {
    Write-Host "`nPublishing client ($($c.Rid))…" -ForegroundColor Cyan
    $pub = Join-Path $out "client-$($c.Rid)"
    dotnet publish $client -c Release -r $c.Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $pub
    if ($LASTEXITCODE) { throw "Client publish failed for $($c.Rid)." }
    Copy-Item (Join-Path $pub $c.Built) (Join-Path $downloads $c.Out) -Force
}

# --- Bundle the installer scripts + metadata ----------------------------------
Write-Host "`nBundling installer…" -ForegroundColor Cyan

# Copy the bash scripts with LF line endings (CRLF breaks them on Linux) and no BOM.
function Copy-ShellScript($name) {
    $text = (Get-Content -Raw (Join-Path $PSScriptRoot $name)) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText((Join-Path $app $name), $text, (New-Object System.Text.UTF8Encoding $false))
}
Copy-ShellScript "install.sh"
Copy-ShellScript "update.sh"
Copy-ShellScript "ztpr.service"

# Bake the repo into update.sh's default so it can find releases without
# ZTPR_REPO. Replace ONLY the default value — the "not configured" guard below
# it keeps the OWNER/REPO sentinel so the script still validates correctly.
if ($Repo) {
    $u = Join-Path $app "update.sh"
    [System.IO.File]::WriteAllText($u,
        ((Get-Content -Raw $u) -replace 'ZTPR_REPO:-OWNER/REPO', "ZTPR_REPO:-$Repo"),
        (New-Object System.Text.UTF8Encoding $false))
}

[System.IO.File]::WriteAllText((Join-Path $app "VERSION"), $Version, (New-Object System.Text.UTF8Encoding $false))

# --- Tar it up ----------------------------------------------------------------
$tarball = Join-Path $root "ztpr-linux-x64.tar.gz"
if (Test-Path $tarball) { Remove-Item -Force $tarball }
Write-Host "`nCreating $tarball…" -ForegroundColor Cyan
tar -C $out -czf $tarball ztpr
if ($LASTEXITCODE) { throw "tar failed." }

$size = "{0:N1} MB" -f ((Get-Item $tarball).Length / 1MB)
Write-Host "`nDone." -ForegroundColor Green
Write-Host "  Bundle folder : $app"
Write-Host "  Tarball       : $tarball ($size)"
Write-Host ""
Write-Host "Deploy:" -ForegroundColor Cyan
Write-Host "  scp `"$tarball`" user@host:~"
Write-Host "  ssh user@host"
Write-Host "  tar xzf ztpr-linux-x64.tar.gz && cd ztpr"
Write-Host "  chmod +x install.sh update.sh && sudo ./install.sh"

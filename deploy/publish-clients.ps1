# Publishes the Ztpr client as self-contained single-file binaries for
# Windows and Linux, placing them where the server's Downloads page serves them.
#
#   pwsh deploy/publish-clients.ps1
#
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "src/Ztpr.Client"
$outDir = Join-Path $root "src/Ztpr.Server/wwwroot/downloads"
New-Item -ItemType Directory -Force $outDir | Out-Null

$targets = @(
    @{ Rid = "win-x64";   Out = "ztpr-win-x64.exe"; Built = "Ztpr.Client.exe" },
    @{ Rid = "linux-x64"; Out = "ztpr-linux-x64";   Built = "Ztpr.Client" }
)

foreach ($t in $targets) {
    Write-Host "Publishing $($t.Rid)…"
    $pub = Join-Path $client "bin/pub/$($t.Rid)"
    dotnet publish $client -c Release -r $t.Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $pub | Out-Null
    Copy-Item (Join-Path $pub $t.Built) (Join-Path $outDir $t.Out) -Force
    Write-Host "  -> $(Join-Path $outDir $t.Out)"
}

Write-Host "Done. Client binaries are in wwwroot/downloads."

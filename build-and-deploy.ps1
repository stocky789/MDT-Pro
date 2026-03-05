# MDT-Pro: Build and deploy directly to your GTA V installation
#
# Builds the plugin, creates the Release package, purges existing MDT Pro from the game,
# then copies the fresh build into the game directory. Preserves the database (data folder).
#
# Does NOT create a ZIP — use build-release.ps1 -CreateZip for distribution.
#
# Usage: .\build-and-deploy.ps1                    # build + deploy to default GTA V path
#        .\build-and-deploy.ps1 -DeployOnly        # deploy existing Release/ (no rebuild)
#        .\build-and-deploy.ps1 -GamePath "D:\Games\GTA V"   # custom game path

param(
    [switch]$DeployOnly,
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
)

$ErrorActionPreference = "Stop"
$RepoRoot  = $PSScriptRoot
$ReleaseDir = Join-Path $RepoRoot "Release"
$GameRoot  = $GamePath
$LSPDFR    = Join-Path $GameRoot "plugins\LSPDFR"
$GameMDTPro = Join-Path $GameRoot "MDTPro"

# --- Build and prepare Release package (unless DeployOnly) ---
if (-not $DeployOnly) {
    & (Join-Path $RepoRoot "build-release.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# --- Validate Release folder exists ---
if (-not (Test-Path $ReleaseDir)) {
    Write-Error "Release folder not found. Run build-release.ps1 first, or omit -DeployOnly."
}

# --- Validate game path ---
if (-not (Test-Path $GameRoot)) {
    Write-Error "Game path not found: $GameRoot`nUse -GamePath to specify your GTA V installation."
}

# --- Purge existing MDT Pro in game (preserve database) ---
Write-Host "Purging existing MDT Pro from game directory (keeping database)..." -ForegroundColor Cyan
$gameData  = Join-Path $GameMDTPro "data"
$dbBackup  = Join-Path $env:TEMP "MDTPro_data_backup"
if (Test-Path $GameMDTPro) {
    if (Test-Path $gameData) {
        Copy-Item $gameData $dbBackup -Recurse -Force
        Write-Host "  Backed up data (database) to $dbBackup"
    }
    Remove-Item $GameMDTPro -Recurse -Force
    Write-Host "  Removed $GameMDTPro"
}
foreach ($name in @("MDTPro.dll", "MDTPro.pdb")) {
    $path = Join-Path $LSPDFR $name
    if (Test-Path $path) {
        Remove-Item $path -Force
        Write-Host "  Removed $path"
    }
}
Write-Host "Purge done." -ForegroundColor Green

# --- Deploy from Release folder ---
Write-Host "Deploying from Release to $GameRoot..." -ForegroundColor Cyan
Copy-Item (Join-Path $ReleaseDir "*") $GameRoot -Recurse -Force -Exclude "README.txt"
Write-Host "  Deployed MDTPro\, plugins\LSPDFR\MDTPro.dll"

# --- Restore database ---
if (Test-Path $dbBackup) {
    $newData = Join-Path $GameMDTPro "data"
    if (-not (Test-Path $newData)) { New-Item $newData -ItemType Directory -Force | Out-Null }
    Copy-Item (Join-Path $dbBackup "*") $newData -Recurse -Force
    Remove-Item $dbBackup -Recurse -Force
    Write-Host "  Restored database (data folder)"
}

Write-Host "Done. MDT Pro deployed (database preserved)." -ForegroundColor Green

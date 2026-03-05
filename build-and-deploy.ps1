# MDT-Pro: Build and deploy to GTA V
# Purges existing MDT Pro in game, then copies a fresh build (saves confusion).
# Usage: .\build-and-deploy.ps1          # build + purge + deploy
#        .\build-and-deploy.ps1 -DeployOnly   # purge + deploy (use existing build)

param([switch]$DeployOnly)

$ErrorActionPreference = "Stop"
$RepoRoot    = $PSScriptRoot
$PluginDir   = Join-Path $RepoRoot "MDTProPlugin"
$Solution    = Join-Path $PluginDir "MDTPro.sln"
$MDTProFolder = Join-Path $RepoRoot "MDTPro"
$GameRoot    = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V"
$LSPDFR      = Join-Path $GameRoot "plugins\LSPDFR"
$DllOutput   = Join-Path $PluginDir "MDTPro\bin\Release\MDTPro.dll"

# --- Build (unless DeployOnly) ---
if (-not $DeployOnly) {
    Write-Host "Building MDT-Pro (Release)..." -ForegroundColor Cyan
    $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
    if (-not $msbuild) {
        $msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    }
    if (-not (Test-Path $msbuild)) {
        Write-Error "MSBuild not found. Build in Visual Studio, then run: .\build-and-deploy.ps1 -DeployOnly"
    }
    & $msbuild $Solution /p:Configuration=Release /t:Rebuild /v:minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Build OK." -ForegroundColor Green
}

# --- Purge existing MDT Pro in game (preserve database) ---
Write-Host "Purging existing MDT Pro from game directory (keeping database)..." -ForegroundColor Cyan
$gameMDTPro = Join-Path $GameRoot "MDTPro"
$gameData = Join-Path $gameMDTPro "data"
$dbBackup = Join-Path $env:TEMP "MDTPro_data_backup"
if (Test-Path $gameMDTPro) {
    if (Test-Path $gameData) {
        Copy-Item $gameData $dbBackup -Recurse -Force
        Write-Host "  Backed up data (database) to $dbBackup"
    }
    Remove-Item $gameMDTPro -Recurse -Force
    Write-Host "  Removed $gameMDTPro"
}
foreach ($name in @("MDTPro.dll", "MDTPro.pdb")) {
    $path = Join-Path $LSPDFR $name
    if (Test-Path $path) {
        Remove-Item $path -Force
        Write-Host "  Removed $path"
    }
}
Write-Host "Purge done." -ForegroundColor Green

# --- Deploy fresh ---
Write-Host "Deploying fresh copy..." -ForegroundColor Cyan
if (-not (Test-Path $DllOutput)) {
    Write-Error "Build output not found: $DllOutput"
}
Copy-Item $DllOutput (Join-Path $LSPDFR "MDTPro.dll") -Force
Write-Host "  Copied MDTPro.dll -> plugins\LSPDFR\"

Copy-Item $MDTProFolder $GameRoot -Recurse -Force
Write-Host "  Copied MDTPro folder -> game root"

if (Test-Path $dbBackup) {
    $newData = Join-Path (Join-Path $GameRoot "MDTPro") "data"
    if (-not (Test-Path $newData)) { New-Item $newData -ItemType Directory -Force | Out-Null }
    Copy-Item (Join-Path $dbBackup "*") $newData -Recurse -Force
    Remove-Item $dbBackup -Recurse -Force
    Write-Host "  Restored database (data folder) into new MDTPro"
}

Write-Host "Done. MDT Pro is purged and redeployed (database preserved)." -ForegroundColor Green

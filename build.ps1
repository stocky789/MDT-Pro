# MDT Pro — single build script.
# Run from repo root: .\build.ps1
#
# Options:
#   .\build.ps1                    Clean rebuild (wipes Release, full build, copies MDTPro web)
#   .\build.ps1 -Incremental       Faster build (no wipe, incremental)
#   .\build.ps1 -Deploy            Build then copy to GTA V (use -GamePath if not default)
#   .\build.ps1 -Deploy -GamePath "D:\Games\GTA V"

param(
    [switch]$Incremental,
    [switch]$Deploy,
    [string]$GamePath = 'C:\Program Files\Rockstar Games\Grand Theft Auto V'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$release = Join-Path $root 'Release'

if (-not $Incremental) {
    Write-Host "Removing Release..."
    if (Test-Path $release) {
        Remove-Item -Path $release -Recurse -Force
    }
}

Write-Host "Building..."
$buildArgs = @(
    (Join-Path $root 'MDTProPlugin\MDTPro.sln'),
    '-c', 'Release',
    '-v', 'minimal'
)
if (-not $Incremental) {
    $buildArgs += '--no-incremental'
}
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Ensure MDTPro web folder is in Release (robocopy in csproj can fail with exit 16)
$mdtDest = Join-Path $release 'MDTPro'
if (-not (Test-Path $mdtDest) -or (Get-ChildItem $mdtDest -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
    Write-Host "Copying MDTPro web to Release..."
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Copy-Item -Path (Join-Path $root 'MDTPro') -Destination $mdtDest -Recurse -Force
}

if ($Deploy) {
    Write-Host "Deploying to $GamePath ..."
    $pluginsDest = Join-Path $GamePath 'plugins\LSPDFR'
    $mdtProDest = Join-Path $GamePath 'MDTPro'
    if (-not (Test-Path $pluginsDest)) {
        Write-Error "Game path not found or missing plugins\LSPDFR: $pluginsDest"
    }
    $dataBackup = Join-Path $env:TEMP ('MDTProDataBackup_' + [guid]::NewGuid().ToString('N'))
    if (Test-Path (Join-Path $mdtProDest 'data')) {
        Write-Host "Backing up MDTPro\data to $dataBackup"
        Copy-Item (Join-Path $mdtProDest 'data') $dataBackup -Recurse
    }
    Copy-Item (Join-Path $release 'plugins\lspdfr\MDTPro.dll') (Join-Path $pluginsDest 'MDTPro.dll') -Force
    if (Test-Path $mdtProDest) { Remove-Item $mdtProDest -Recurse -Force }
    Copy-Item (Join-Path $release 'MDTPro') $mdtProDest -Recurse -Force
    if (Test-Path $dataBackup) {
        Write-Host "Restoring MDTPro\data"
        Copy-Item $dataBackup (Join-Path $mdtProDest 'data') -Recurse -Force
        Remove-Item $dataBackup -Recurse -Force
    }
    Write-Host "Deployed."
}

Write-Host "Done. Output: $release"

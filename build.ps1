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

# MDT Pro — builds the full mod into Release\ (plugin DLL + MDTPro web).
# Run from repo root: .\build.ps1
# -Deploy copies Release into your GTA V folder. -GamePath "D:\Games\GTA V" if not default.

param(
    [switch]$Incremental,
    [switch]$Deploy,
    [string]$GamePath = 'C:\Program Files\Rockstar Games\Grand Theft Auto V'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$release = Join-Path $root 'Release'
# DLL lands in bin\Release\ or bin\Release\net48\ depending on SDK/project
$dllDestDir = Join-Path $release 'plugins\lspdfr'
$dllDest = Join-Path $dllDestDir 'MDTPro.dll'
$mdtSource = Join-Path $root 'MDTPro'
$mdtDest = Join-Path $release 'MDTPro'

# 1) Wipe Release (unless incremental)
if (-not $Incremental) {
    if (Test-Path $release) {
        Remove-Item -Path $release -Recurse -Force
    }
}

# 2) Build the plugin
Write-Host "Building MDTPro.dll..."
$buildArgs = @( (Join-Path $root 'MDTProPlugin\MDTPro.sln'), '-c', 'Release', '-v', 'minimal' )
if (-not $Incremental) { $buildArgs += '--no-incremental' }
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit $LASTEXITCODE }

# 3) Find DLL (SDK can output to bin\Release\ or bin\Release\net48\)
$dllNet48 = Join-Path $root 'MDTProPlugin\MDTPro\bin\Release\net48\MDTPro.dll'
$dllRelease = Join-Path $root 'MDTProPlugin\MDTPro\bin\Release\MDTPro.dll'
if (Test-Path $dllNet48) { $dllSource = $dllNet48 }
elseif (Test-Path $dllRelease) { $dllSource = $dllRelease }
else {
    Write-Error "MDTPro.dll not found in bin\Release\ or bin\Release\net48\"
    exit 1
}

# 4) Put entire release into Release\
New-Item -ItemType Directory -Path $dllDestDir -Force | Out-Null
Copy-Item -Path $dllSource -Destination $dllDest -Force
Write-Host "  -> $dllDest"

if (Test-Path $mdtDest) { Remove-Item $mdtDest -Recurse -Force }
Copy-Item -Path $mdtSource -Destination $mdtDest -Recurse -Force
Write-Host "  -> $mdtDest (web MDT)"

# 5) Deploy to game if requested
if ($Deploy) {
    $pluginsDest = Join-Path $GamePath 'plugins\LSPDFR'
    $mdtProDest = Join-Path $GamePath 'MDTPro'
    if (-not (Test-Path $pluginsDest)) { Write-Error "Game path not found: $pluginsDest"; exit 1 }
    $dataBackup = Join-Path $env:TEMP ('MDTProDataBackup_' + [guid]::NewGuid().ToString('N'))
    if (Test-Path (Join-Path $mdtProDest 'data')) { Copy-Item (Join-Path $mdtProDest 'data') $dataBackup -Recurse }
    Copy-Item $dllDest (Join-Path $pluginsDest 'MDTPro.dll') -Force
    if (Test-Path $mdtProDest) { Remove-Item $mdtProDest -Recurse -Force }
    Copy-Item $mdtDest $mdtProDest -Recurse -Force
    if (Test-Path $dataBackup) { Copy-Item $dataBackup (Join-Path $mdtProDest 'data') -Recurse -Force; Remove-Item $dataBackup -Recurse -Force }
    Write-Host "Deployed to $GamePath"
}

Write-Host "Done. Full mod release: $release"
Write-Host "  Release\plugins\lspdfr\MDTPro.dll"
Write-Host "  Release\MDTPro\"

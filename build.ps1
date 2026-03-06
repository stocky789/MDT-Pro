# MDT Pro — builds the full mod into Release\ (plugin DLL + MDTPro web + dependencies).
# Run from repo root: .\build.ps1
#
# Options:
#   .\build.ps1                    Clean rebuild (wipes Release, full build)
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
$dllDestDir = Join-Path $release 'plugins\lspdfr'
$dllDest = Join-Path $dllDestDir 'MDTPro.dll'
$mdtSource = Join-Path $root 'MDTPro'
$mdtDest = Join-Path $release 'MDTPro'

# SQLite dependency paths - check multiple locations:
# 1. Dependencies folder (committed to repo for easy builds)
# 2. NuGet packages folder (if restored)
# 3. Build output folder (if copied during build)
$depsFolder = Join-Path $root 'Dependencies'
$sqlitePackage = Join-Path $root 'MDTProPlugin\packages\System.Data.SQLite.Core.1.0.119.0'

$sqliteDllPaths = @(
    (Join-Path $depsFolder 'System.Data.SQLite.dll'),
    (Join-Path $sqlitePackage 'lib\net46\System.Data.SQLite.dll')
)
$sqliteInteropPaths = @(
    (Join-Path $depsFolder 'x64\SQLite.Interop.dll'),
    (Join-Path $sqlitePackage 'build\net46\x64\SQLite.Interop.dll')
)

# 1) Wipe Release (unless incremental)
if (-not $Incremental) {
    Write-Host "Removing Release..."
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

# 4) Copy plugin DLL and web folder to Release\
New-Item -ItemType Directory -Path $dllDestDir -Force | Out-Null
Copy-Item -Path $dllSource -Destination $dllDest -Force
Write-Host "  -> $dllDest"

if (Test-Path $mdtDest) { Remove-Item $mdtDest -Recurse -Force }
Copy-Item -Path $mdtSource -Destination $mdtDest -Recurse -Force
Write-Host "  -> $mdtDest (web MDT)"

# 5) Copy SQLite dependencies to plugins\lspdfr\ (required for database functionality)
$sqliteDllPaths += (Join-Path (Split-Path $dllSource) 'System.Data.SQLite.dll')
$sqliteInteropPaths += (Join-Path (Split-Path $dllSource) 'x64\SQLite.Interop.dll')

$sqliteDllSource = $sqliteDllPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
$sqliteInteropSource = $sqliteInteropPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($sqliteDllSource) {
    Copy-Item -Path $sqliteDllSource -Destination $dllDestDir -Force
    Write-Host "  -> $(Join-Path $dllDestDir 'System.Data.SQLite.dll')"
} else {
    Write-Warning "System.Data.SQLite.dll not found. Place it in Dependencies\ or run NuGet restore."
}

$x64Dir = Join-Path $dllDestDir 'x64'
if ($sqliteInteropSource) {
    New-Item -ItemType Directory -Path $x64Dir -Force | Out-Null
    Copy-Item -Path $sqliteInteropSource -Destination $x64Dir -Force
    Write-Host "  -> $(Join-Path $x64Dir 'SQLite.Interop.dll')"
} else {
    Write-Warning "SQLite.Interop.dll not found. Place it in Dependencies\x64\ or run NuGet restore."
}

# 6) Deploy to game if requested
if ($Deploy) {
    $pluginsDest = Join-Path $GamePath 'plugins\LSPDFR'
    $mdtProDest = Join-Path $GamePath 'MDTPro'
    $pluginsX64 = Join-Path $pluginsDest 'x64'
    if (-not (Test-Path $pluginsDest)) { Write-Error "Game path not found: $pluginsDest"; exit 1 }
    
    # Backup user data
    $dataBackup = Join-Path $env:TEMP ('MDTProDataBackup_' + [guid]::NewGuid().ToString('N'))
    if (Test-Path (Join-Path $mdtProDest 'data')) { Copy-Item (Join-Path $mdtProDest 'data') $dataBackup -Recurse }
    
    # Deploy plugin DLL
    Copy-Item $dllDest (Join-Path $pluginsDest 'MDTPro.dll') -Force
    
    # Deploy web folder
    if (Test-Path $mdtProDest) { Remove-Item $mdtProDest -Recurse -Force }
    Copy-Item $mdtDest $mdtProDest -Recurse -Force
    
    # Deploy SQLite dependencies to plugins\LSPDFR\
    if ($sqliteDllSource) {
        Copy-Item -Path $sqliteDllSource -Destination $pluginsDest -Force
    }
    if ($sqliteInteropSource) {
        if (-not (Test-Path $pluginsX64)) { New-Item -ItemType Directory -Path $pluginsX64 -Force | Out-Null }
        Copy-Item -Path $sqliteInteropSource -Destination $pluginsX64 -Force
    }
    
    # Restore user data
    if (Test-Path $dataBackup) { Copy-Item $dataBackup (Join-Path $mdtProDest 'data') -Recurse -Force; Remove-Item $dataBackup -Recurse -Force }
    Write-Host "Deployed to $GamePath"
}

Write-Host "Done. Full mod release: $release"
Write-Host "  Release\plugins\lspdfr\MDTPro.dll"
Write-Host "  Release\plugins\lspdfr\System.Data.SQLite.dll"
Write-Host "  Release\plugins\lspdfr\x64\SQLite.Interop.dll"
Write-Host "  Release\MDTPro\"

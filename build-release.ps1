# MDT-Pro: Build and package a release for distribution
#
# Creates Release/ folder with the same structure as GTA V (MDTPro/, plugins\LSPDFR\MDTPro.dll).
# Does NOT modify your game directory — use build-and-deploy.ps1 for that.
#
# Usage: .\build-release.ps1
#        .\build-release.ps1 -CreateZip   # also creates MDT-Pro-v0.9.0.zip

param([switch]$CreateZip)

$ErrorActionPreference = "Stop"
$RepoRoot     = $PSScriptRoot
$PluginDir    = Join-Path $RepoRoot "MDTProPlugin"
$Solution     = Join-Path $PluginDir "MDTPro.sln"
$MDTProSource = Join-Path $RepoRoot "MDTPro"
$DllOutput    = Join-Path $PluginDir "MDTPro\bin\Release\MDTPro.dll"
$ReleaseDir   = Join-Path $RepoRoot "Release"

# Runtime files to exclude from MDTPro folder (created by plugin on first run)
$ExcludeFromMDTPro = @(
    "data",
    "config.json",
    "language.json",
    "citationOptions.json",
    "arrestOptions.json",
    "ipAddresses.txt",
    "MDTPro.log"
)

# --- Read version from AssemblyInfo (major.minor.patch) ---
$asmInfo = Get-Content (Join-Path $PluginDir "MDTPro\Properties\AssemblyInfo.cs") -Raw
if ($asmInfo -match 'AssemblyVersion\("([^"]+)"\)') {
    $v = $Matches[1]; $parts = $v.Split('.'); $Version = ($parts[0..2] -join '.')
} else {
    $Version = "0.9.0"
}

# --- Build ---
Write-Host "Building MDT-Pro (Release)..." -ForegroundColor Cyan
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
if (-not $msbuild) { $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" }
if (-not (Test-Path $msbuild)) {
    Write-Error "MSBuild not found. Build in Visual Studio first, then run this script again."
}
& $msbuild $Solution /p:Configuration=Release /t:Rebuild /v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Build OK." -ForegroundColor Green

# --- Prepare Release folder ---
Write-Host "Preparing Release folder..." -ForegroundColor Cyan
if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item $ReleaseDir -ItemType Directory | Out-Null

# --- Copy MDTPro folder (excluding runtime files) ---
$mdtProDest = Join-Path $ReleaseDir "MDTPro"
Copy-Item $MDTProSource $mdtProDest -Recurse -Force
foreach ($name in $ExcludeFromMDTPro) {
    $path = Join-Path $mdtProDest $name
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $path -Force -ErrorAction SilentlyContinue
        Write-Host "  Excluded: MDTPro/$name"
    }
}

# --- Copy plugin DLL to plugins/LSPDFR ---
$pluginsLspdfr = Join-Path $ReleaseDir "plugins\LSPDFR"
New-Item $pluginsLspdfr -ItemType Directory -Force | Out-Null
if (-not (Test-Path $DllOutput)) { Write-Error "Build output not found: $DllOutput" }
Copy-Item $DllOutput (Join-Path $pluginsLspdfr "MDTPro.dll") -Force
Write-Host "  Copied MDTPro.dll -> plugins\LSPDFR\"

# --- Ensure MDTPro.ini exists in MDTPro folder (settings / keybind config; plugin reads MDTProPath/MDTPro.ini) ---
$iniPath = Join-Path $mdtProDest "MDTPro.ini"
$iniContent = @"
; MDT Pro - In-game plugin settings
; Edit this file to change keybinds and other options. Restart the game or go off/on duty for changes to take effect.

[MDTPro]
; Key to open the in-game Settings menu (ALPR and other options). Use key names like F7, F8, F9, F10, etc.
SettingsMenuKey=F7
"@
Set-Content -Path $iniPath -Value $iniContent -Encoding UTF8
Write-Host "  Created MDTPro\MDTPro.ini"

# --- Write install README ---
$readme = @"
MDT Pro v$Version - Installation
================================

1. Copy the entire contents of this folder into your GTA V main directory.
   (The folder structure mirrors GTA V: MDTPro and plugins\LSPDFR\MDTPro.dll.)

2. Requirements:
   - LSPDFR
   - CommonDataFramework
   - CalloutInterfaceAPI
   - RagePluginHook

3. Go on duty in LSPDFR. In-game notifications will show the MDT address.
   Access MDT Pro from any browser (e.g. http://127.0.0.1:8080).
   Chromium-based browsers (Chrome, Brave) work best.

4. Optional - Steam overlay: Set your overlay browser home page to the
   address shown by MDT Pro for quick access in-game.
"@
$readme | Out-File (Join-Path $ReleaseDir "README.txt") -Encoding UTF8

Write-Host "  Created README.txt"
Write-Host "Release folder ready: $ReleaseDir" -ForegroundColor Green

# --- Create ZIP ---
if ($CreateZip) {
    $zipName = "MDT-Pro-v$Version.zip"
    $zipPath = Join-Path $RepoRoot $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $ReleaseDir "*") -DestinationPath $zipPath -Force
    Write-Host "Created ZIP: $zipPath" -ForegroundColor Green
}

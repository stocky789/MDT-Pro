# MDT Pro — builds the full mod into Release\ (plugin DLL + MDTPro web + dependencies).
# Run from repo root: .\build.ps1
#
# Options:
#   .\build.ps1                    Clean rebuild (wipes Release, full build)
#   .\build.ps1 -Incremental       Faster build (no wipe, incremental)
#   .\build.ps1 -Deploy            Build then copy to GTA V (use -GamePath if not default)
#   .\build.ps1 -Deploy -GamePath "D:\Games\GTA V"
#   GamePath is also used to copy DamageTrackingFramework.dll into Release (required for injury reports; or use References\DamageTrackingFramework.dll).
#   OpenIV packages (install + uninstall) are always created in Release\

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

# 3) Find DLL (csproj may output directly to Release\, or to bin\Release\)
$dllDirect = Join-Path $root 'Release\plugins\lspdfr\MDTPro.dll'
$dllNet48 = Join-Path $root 'MDTProPlugin\MDTPro\bin\Release\net48\MDTPro.dll'
$dllRelease = Join-Path $root 'MDTProPlugin\MDTPro\bin\Release\MDTPro.dll'

if (Test-Path $dllDirect) {
    # DLL already in Release (csproj OutputPath)
    $dllSource = $dllDirect
    Write-Host "  -> $dllDest (built directly)"
} elseif (Test-Path $dllNet48) {
    $dllSource = $dllNet48
    New-Item -ItemType Directory -Path $dllDestDir -Force | Out-Null
    Copy-Item -Path $dllSource -Destination $dllDest -Force
    Write-Host "  -> $dllDest"
} elseif (Test-Path $dllRelease) {
    $dllSource = $dllRelease
    New-Item -ItemType Directory -Path $dllDestDir -Force | Out-Null
    Copy-Item -Path $dllSource -Destination $dllDest -Force
    Write-Host "  -> $dllDest"
} else {
    Write-Error "MDTPro.dll not found in Release\, bin\Release\, or bin\Release\net48\"
    exit 1
}

# 4) Copy web folder to Release\

if (Test-Path $mdtDest) { Remove-Item $mdtDest -Recurse -Force }
Copy-Item -Path $mdtSource -Destination $mdtDest -Recurse -Force
Write-Host "  -> $mdtDest (web MDT)"

# 5) Copy Dependencies folder into Release (SQLite, x64\, etc.). Exclude optional/unused DLLs if present.
$releaseRoot = $release
$depsExclude = @('DamageTrackerLib.dll', 'DamageTrackingFramework.dll')
if (Test-Path $depsFolder) {
    Get-ChildItem -Path $depsFolder -Force | ForEach-Object {
        if ($depsExclude -contains $_.Name) { return }
        $dest = Join-Path $releaseRoot $_.Name
        if ($_.PSIsContainer) {
            if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
            Copy-Item -Path $_.FullName -Destination $dest -Recurse -Force
            Write-Host "  -> $dest (from Dependencies\$($_.Name))"
        } else {
            Copy-Item -Path $_.FullName -Destination $dest -Force
            Write-Host "  -> $dest (from Dependencies\$($_.Name))"
        }
    }
} else {
    Write-Warning "Dependencies folder not found at $depsFolder"
}

# 6) If SQLite files weren't in Dependencies, try NuGet/build output and copy to Release root and Release\x64
$sqliteDllPaths += (Join-Path (Split-Path $dllSource) 'System.Data.SQLite.dll')
$sqliteInteropPaths += (Join-Path (Split-Path $dllSource) 'x64\SQLite.Interop.dll')

$sqliteDllSource = $sqliteDllPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
$sqliteInteropSource = $sqliteInteropPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$releaseX64 = Join-Path $releaseRoot 'x64'

if ($sqliteDllSource) {
    Copy-Item -Path $sqliteDllSource -Destination $releaseRoot -Force
    Write-Host "  -> $(Join-Path $releaseRoot 'System.Data.SQLite.dll')"
} else {
    Write-Warning "System.Data.SQLite.dll not found. Place it in Dependencies\ or run NuGet restore."
}

if ($sqliteInteropSource) {
    New-Item -ItemType Directory -Path $releaseX64 -Force | Out-Null
    Copy-Item -Path $sqliteInteropSource -Destination $releaseX64 -Force
    Write-Host "  -> $(Join-Path $releaseX64 'SQLite.Interop.dll')"
} else {
    Write-Warning "SQLite.Interop.dll not found. Place it in Dependencies\x64\ or run NuGet restore."
}

# 7) Deploy to game if requested
if ($Deploy) {
    $pluginsDest = Join-Path $GamePath 'plugins\LSPDFR'
    $mdtProDest = Join-Path $GamePath 'MDTPro'
    $gameRootX64 = Join-Path $GamePath 'x64'
    if (-not (Test-Path $pluginsDest)) { Write-Error "Game path not found: $pluginsDest"; exit 1 }

    # Backup user data
    $dataBackup = Join-Path $env:TEMP ('MDTProDataBackup_' + [guid]::NewGuid().ToString('N'))
    if (Test-Path (Join-Path $mdtProDest 'data')) { Copy-Item (Join-Path $mdtProDest 'data') $dataBackup -Recurse }

    # Deploy plugin DLL
    Copy-Item $dllDest (Join-Path $pluginsDest 'MDTPro.dll') -Force

    # Deploy web folder
    if (Test-Path $mdtProDest) { Remove-Item $mdtProDest -Recurse -Force }
    Copy-Item $mdtDest $mdtProDest -Recurse -Force

    # Deploy SQLite to GTA V root (native loader requires app directory)
    if ($sqliteDllSource) {
        Copy-Item -Path $sqliteDllSource -Destination $GamePath -Force
    }
    if ($sqliteInteropSource) {
        New-Item -ItemType Directory -Path $gameRootX64 -Force | Out-Null
        Copy-Item -Path $sqliteInteropSource -Destination $gameRootX64 -Force
    }

    # Restore user data
    if (Test-Path $dataBackup) { Copy-Item $dataBackup (Join-Path $mdtProDest 'data') -Recurse -Force; Remove-Item $dataBackup -Recurse -Force }
    Write-Host "Deployed to $GamePath"
}

# 8) Create OpenIV package (always)
    Write-Host "Creating OpenIV package..."
    $asmInfo = Join-Path $root 'MDTProPlugin\MDTPro\Properties\AssemblyInfo.cs'
    $versionMatch = Select-String -Path $asmInfo -Pattern '^\s*\[assembly:\s*AssemblyVersion\("([^"]+)"\)' | Select-Object -First 1
    $versionStr = if ($versionMatch) { $versionMatch.Matches.Groups[1].Value } else { '0.9.0.0' }
    $versionStr = $versionStr -replace '\*', '0'  # sanitize for file paths
    # OIV uses major.minor (tag) - use full version as tag so OpenIV shows "0.0 (0.9.5.0)" i.e. readable 0.9.5.0
    $versionParts = $versionStr.Split('.')
    $major = if ($versionParts.Length -gt 0) { [int]$versionParts[0] } else { 0 }
    $minor = if ($versionParts.Length -gt 1) { [int]$versionParts[1] } else { 9 }
    $tag = $versionStr  # full version in tag e.g. "0.9.5.0"

    $oivDir = Join-Path $release 'oivBuild'
    $oivContent = Join-Path $oivDir 'content'
    if (Test-Path $oivDir) { Remove-Item $oivDir -Recurse -Force }
    New-Item -ItemType Directory -Path $oivContent -Force | Out-Null

    # Content structure for OIV (paths relative to GTA V root)
    $pluginsOiv = Join-Path $oivContent 'plugins\LSPDFR'
    New-Item -ItemType Directory -Path $pluginsOiv -Force | Out-Null
    Copy-Item -Path $dllDest -Destination (Join-Path $pluginsOiv 'MDTPro.dll') -Force
    $newtonsoftDll = Join-Path $dllDestDir 'Newtonsoft.Json.dll'
    if (Test-Path $newtonsoftDll) { Copy-Item -Path $newtonsoftDll -Destination (Join-Path $pluginsOiv 'Newtonsoft.Json.dll') -Force }
    if ($sqliteDllSource) { Copy-Item -Path $sqliteDllSource -Destination (Join-Path $oivContent 'System.Data.SQLite.dll') -Force }
    if ($sqliteInteropSource) {
        $x64Oiv = Join-Path $oivContent 'x64'
        New-Item -ItemType Directory -Path $x64Oiv -Force | Out-Null
        Copy-Item -Path $sqliteInteropSource -Destination (Join-Path $x64Oiv 'SQLite.Interop.dll') -Force
    }
    Copy-Item -Path $mdtDest -Destination (Join-Path $oivContent 'MDTPro') -Recurse -Force

    # Generate assembly.xml add and delete commands for MDTPro folder (OIV uses forward slashes)
    $mdtAdds = @()
    $mdtDeletes = @()
    Get-ChildItem -Path (Join-Path $oivContent 'MDTPro') -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($oivContent.Length + 1).Replace('\', '/')
        $mdtAdds += "    <add source=`"$rel`">$rel</add>"
        $mdtDeletes += "    <delete>$rel</delete>"
    }

    $pluginAdds = "    <add source=`"plugins/LSPDFR/MDTPro.dll`">plugins/LSPDFR/MDTPro.dll</add>`n    <add source=`"plugins/LSPDFR/Newtonsoft.Json.dll`">plugins/LSPDFR/Newtonsoft.Json.dll</add>"
    $contentXml = @"
$pluginAdds
    <add source="System.Data.SQLite.dll">System.Data.SQLite.dll</add>
    <add source="x64/SQLite.Interop.dll">x64/SQLite.Interop.dll</add>
$($mdtAdds -join "`n")
"@

    $assemblyXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<package version="2.2" id="{E8A7C4B2-9D1F-4E6A-8B3C-2F5A1D9E7B4C}" target="Five">
  <metadata>
    <name>MDT Pro</name>
    <version>
      <major>$major</major>
      <minor>$minor</minor>
      <tag>$tag</tag>
    </version>
    <author>
      <displayName>stocky789</displayName>
    </author>
    <description footerLink="https://www.lcpdfr.com/downloads/gta5mods/scripts/53627-mdtpro" footerLinkTitle="LCPDFR">
      <![CDATA[Police MDT (Mobile Data Terminal) plugin for LSPDFR. Runs a local web server when you go on duty. Requires LSPDFR, CommonDataFramework, CalloutInterfaceAPI, and Policing Redefined. Install with OpenIV Package Installer.]]>
    </description>
    <largeDescription displayName="GitHub" footerLink="https://github.com/stocky789/MDT-Pro" footerLinkTitle="GitHub">
      <![CDATA[Source code and releases: https://github.com/stocky789/MDT-Pro]]>
    </largeDescription>
  </metadata>
  <colors>
    <headerBackground useBlackTextColor="False">`$FF1a365d</headerBackground>
    <iconBackground>`$FF2c5282</iconBackground>
  </colors>
  <content>
$contentXml
  </content>
</package>
"@
    $assemblyXml | Out-File -FilePath (Join-Path $oivDir 'assembly.xml') -Encoding UTF8

    # Optional: copy 128x128 icon if available
    $iconSrc = Join-Path $root 'favicon\MDT Pro.png'
    if (Test-Path $iconSrc) {
        Copy-Item -Path $iconSrc -Destination (Join-Path $oivDir 'icon.png') -Force
    }

    # Create .oiv (ZIP) - OIV is a ZIP archive; create as .zip then rename to .oiv
    $oivZip = Join-Path $release "MDTPro-$versionStr.zip"
    $oivOut = Join-Path $release "MDTPro-$versionStr.oiv"
    if (Test-Path $oivZip) { Remove-Item $oivZip -Force }
    if (Test-Path $oivOut) { Remove-Item $oivOut -Force }
    $oivItems = @( (Join-Path $oivDir 'assembly.xml'), (Join-Path $oivDir 'content') )
    if (Test-Path (Join-Path $oivDir 'icon.png')) { $oivItems += Join-Path $oivDir 'icon.png' }
    Compress-Archive -Path $oivItems -DestinationPath $oivZip -CompressionLevel Optimal -Force
    Rename-Item -Path $oivZip -NewName (Split-Path $oivOut -Leaf) -Force
    Write-Host "  -> $oivOut (OpenIV package)"

    # Create uninstaller OIV package (delete commands only; unique GUID)
    # ONLY remove MDT Pro files. Do NOT remove shared dependencies (SQLite, Newtonsoft.Json, DamageTrackingFramework)
    # so other mods that use them keep working after uninstall.
    $runtimeDeletes = @(
        'MDTPro/config.json', 'MDTPro/language.json', 'MDTPro/citationOptions.json', 'MDTPro/arrestOptions.json',
        'MDTPro/MDTPro.log', 'MDTPro/ipAddresses.txt',
        'MDTPro/data/mdtpro.db', 'MDTPro/data/mdtpro.db-wal', 'MDTPro/data/mdtpro.db-shm',
        'MDTPro/data/peds.json', 'MDTPro/data/peds.json.bak',
        'MDTPro/data/vehicles.json', 'MDTPro/data/vehicles.json.bak',
        'MDTPro/data/court.json', 'MDTPro/data/court.json.bak',
        'MDTPro/data/shiftHistory.json', 'MDTPro/data/shiftHistory.json.bak',
        'MDTPro/data/officerInformation.json', 'MDTPro/data/officerInformation.json.bak',
        'MDTPro/data/reports/incidentReports.json', 'MDTPro/data/reports/citationReports.json', 'MDTPro/data/reports/arrestReports.json'
    ) | ForEach-Object { "    <delete>$_</delete>" }
    $deleteXml = @"
    <delete>plugins/LSPDFR/MDTPro.dll</delete>
$($mdtDeletes -join "`n")
$($runtimeDeletes -join "`n")
"@
    $uninstallAssembly = @"
<?xml version="1.0" encoding="UTF-8"?>
<package version="2.2" id="{A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}" target="Five">
  <metadata>
    <name>MDT Pro - Uninstall</name>
    <version>
      <major>$major</major>
      <minor>$minor</minor>
      <tag>$tag</tag>
    </version>
    <author>
      <displayName>stocky789</displayName>
    </author>
    <description footerLink="https://www.lcpdfr.com/downloads/gta5mods/scripts/53627-mdtpro" footerLinkTitle="LCPDFR">
      <![CDATA[Removes only MDT Pro (plugin DLL and MDTPro folder). Leaves shared dependencies (SQLite, Newtonsoft.Json, DamageTrackingFramework) so other mods keep working. For a full purge, manually delete the MDTPro folder and any dependencies if no other mod needs them.]]>
    </description>
    <largeDescription displayName="GitHub" footerLink="https://github.com/stocky789/MDT-Pro" footerLinkTitle="GitHub">
      <![CDATA[Source code and releases: https://github.com/stocky789/MDT-Pro]]>
    </largeDescription>
  </metadata>
  <colors>
    <headerBackground useBlackTextColor="False">`$FF4a1515</headerBackground>
    <iconBackground>`$FF6b2121</iconBackground>
  </colors>
  <content>
$deleteXml
  </content>
</package>
"@
    $oivUninstallDir = Join-Path $release 'oivUninstall'
    if (Test-Path $oivUninstallDir) { Remove-Item $oivUninstallDir -Recurse -Force }
    New-Item -ItemType Directory -Path $oivUninstallDir -Force | Out-Null
    $uninstallAssembly | Out-File -FilePath (Join-Path $oivUninstallDir 'assembly.xml') -Encoding UTF8
    if (Test-Path $iconSrc) { Copy-Item -Path $iconSrc -Destination (Join-Path $oivUninstallDir 'icon.png') -Force }
    # OpenIV requires a content folder; Compress-Archive skips empty dirs, so add placeholder
    $uninstallContentDir = Join-Path $oivUninstallDir 'content'
    New-Item -ItemType Directory -Path $uninstallContentDir -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $uninstallContentDir '.gitkeep'), '')
    $oivUninstallZip = Join-Path $release "MDTPro-$versionStr-Uninstall.zip"
    $oivUninstallOut = Join-Path $release "MDTPro-$versionStr-Uninstall.oiv"
    if (Test-Path $oivUninstallZip) { Remove-Item $oivUninstallZip -Force }
    if (Test-Path $oivUninstallOut) { Remove-Item $oivUninstallOut -Force }
    $uninstallOivItems = @( (Join-Path $oivUninstallDir 'assembly.xml'), (Join-Path $oivUninstallDir 'content') )
    if (Test-Path (Join-Path $oivUninstallDir 'icon.png')) { $uninstallOivItems += Join-Path $oivUninstallDir 'icon.png' }
    Compress-Archive -Path $uninstallOivItems -DestinationPath $oivUninstallZip -CompressionLevel Optimal -Force
    Rename-Item -Path $oivUninstallZip -NewName (Split-Path $oivUninstallOut -Leaf) -Force
    Remove-Item -Path $oivUninstallDir -Recurse -Force
    Remove-Item -Path $oivDir -Recurse -Force
    Write-Host "  -> $oivUninstallOut (OpenIV uninstaller)"

# README with install instructions (always)
$readmePath = Join-Path $release 'README.txt'
@"
MDT Pro - Install instructions
===============================

OPENIV INSTALL (recommended)
----------------------------
- MDTPro-*.oiv = Install package. In OpenIV: Edit mode -> drag the .oiv onto OpenIV, or Tools -> Package Installer, then install.
- MDTPro-*-Uninstall.oiv = Uninstall package. Use this to remove the mod via OpenIV.


MANUAL INSTALL (no OpenIV)
-------------------------
Copy everything from this folder into your GTA V folder (the folder containing GTA5.exe), EXCEPT the .oiv files.

Copy:
  - plugins\   (into GTA V\plugins\)
  - MDTPro\    (into GTA V\MDTPro\)
  - x64\       (into GTA V\x64\)
  - System.Data.SQLite.dll  (into GTA V root)

Do NOT copy the .oiv files into your game folder; they are only for OpenIV.

Requirements: LSPDFR, RagePluginHook, Common Data Framework. See the mod page for full requirements.
"@ | Out-File -FilePath $readmePath -Encoding UTF8
Write-Host "  -> $readmePath (install instructions)"

Write-Host "Done. Full mod release: $release"
Write-Host "  Release\plugins\lspdfr\MDTPro.dll"
Write-Host "  Release\System.Data.SQLite.dll (GTA V root)"
Write-Host "  Release\x64\SQLite.Interop.dll (GTA V root\x64)"
Write-Host "  Release\MDTPro\"

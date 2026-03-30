# MDT Pro — builds the full mod into Release\ (plugin DLL + MDTPro web + dependencies),
# then mirrors that drop into Native Release\ and publishes the Windows MDC desktop app there.
# Run from repo root: .\build.ps1
#
# Options:
#   .\build.ps1                    Clean rebuild (wipes Release, full build; Native Release\ rebuilt at end)
#   .\build.ps1 -Incremental       Faster build (no wipe, incremental)
#   .\build.ps1 -SkipWindowsApp    Build mod only; skip native WPF publish (still mirrors Release → Native Release)
#   .\build.ps1 -Deploy            Build then copy to GTA V (use -GamePath if not default)
#   .\build.ps1 -Deploy -GamePath "D:\Games\GTA V"
#   GamePath is also used to copy DamageTrackingFramework.dll into Release (required for injury reports; or use References\DamageTrackingFramework.dll).
#   OpenIV packages (install + uninstall) are always created in Release\ (and copied to Native Release\).
#
# Native Release\ layout:
#   Same contents as Release\ (game mod, .oiv, README, LICENSE) plus MDTProNative\ (win-x64 published desktop app).

param(
    [switch]$Incremental,
    [switch]$Deploy,
    [switch]$SkipWindowsApp,
    [string]$GamePath = 'C:\Program Files\Rockstar Games\Grand Theft Auto V'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
# Always run from repo root (works even if you invoked the script from another directory).
Set-Location -LiteralPath $root

$release = Join-Path $root 'Release'
$nativeReleaseDir = Join-Path $root 'Native Release'
$legacyReleasesDir = Join-Path $root 'Releases'
$nativeWpfProj = Join-Path $root 'native\src\MDTProNative.Wpf\MDTProNative.Wpf.csproj'
$nativePublishDir = Join-Path $nativeReleaseDir 'MDTProNative'
$dllDestDir = Join-Path $release 'plugins\lspdfr'
$dllDest = Join-Path $dllDestDir 'MDTPro.dll'
$mdtSource = Join-Path $root 'MDTPro'
$mdtDest = Join-Path $release 'MDTPro'

# SQLite dependency paths - check multiple locations:
# 1. Dependencies folder (committed to repo for easy builds)
# 2. NuGet packages folder (if restored) - discover version dynamically
# 3. Build output folder (if copied during build)
$depsFolder = Join-Path $root 'Dependencies'
$packagesDir = Join-Path $root 'MDTProPlugin\packages'
$sqlitePkg = Get-ChildItem -Path $packagesDir -Directory -Filter 'System.Data.SQLite.Core*' -ErrorAction SilentlyContinue | Select-Object -First 1
$sqlitePackageDir = if ($sqlitePkg) { $sqlitePkg.FullName } else { $null }

$sqliteDllPaths = @(
    (Join-Path $depsFolder 'System.Data.SQLite.dll')
)
if ($sqlitePackageDir) {
    $sqliteDllPaths += (Join-Path $sqlitePackageDir 'lib\net46\System.Data.SQLite.dll')
    $sqliteDllPaths += (Join-Path $sqlitePackageDir 'lib\net48\System.Data.SQLite.dll')
}

$sqliteInteropPaths = @(
    (Join-Path $depsFolder 'x64\SQLite.Interop.dll')
)
if ($sqlitePackageDir) {
    $sqliteInteropPaths += (Join-Path $sqlitePackageDir 'build\net46\x64\SQLite.Interop.dll')
    $sqliteInteropPaths += (Join-Path $sqlitePackageDir 'build\net48\x64\SQLite.Interop.dll')
}

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
    # Copy private dependencies (Newtonsoft.Json etc.) when build outputs to bin
    $binDir = Split-Path $dllNet48
    foreach ($dep in @('Newtonsoft.Json.dll')) {
        $src = Join-Path $binDir $dep
        if (Test-Path $src) { Copy-Item -Path $src -Destination (Join-Path $dllDestDir $dep) -Force }
    }
    Write-Host "  -> $dllDest"
} elseif (Test-Path $dllRelease) {
    $dllSource = $dllRelease
    New-Item -ItemType Directory -Path $dllDestDir -Force | Out-Null
    Copy-Item -Path $dllSource -Destination $dllDest -Force
    $binDir = Split-Path $dllRelease
    foreach ($dep in @('Newtonsoft.Json.dll')) {
        $src = Join-Path $binDir $dep
        if (Test-Path $src) { Copy-Item -Path $src -Destination (Join-Path $dllDestDir $dep) -Force }
    }
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

# 6b) Validate required files for OpenIV package - fail early if SQLite is missing
# (Users installing via .oiv report "SQL stops working" when these DLLs are absent)
$newtonsoftInRelease = Join-Path $dllDestDir 'Newtonsoft.Json.dll'
$sqliteInRelease = Join-Path $releaseRoot 'System.Data.SQLite.dll'
$interopInRelease = Join-Path $releaseX64 'SQLite.Interop.dll'

$oivRequired = @(
    @{ Path = $dllDest; Name = 'MDTPro.dll' }
    @{ Path = $newtonsoftInRelease; Name = 'Newtonsoft.Json.dll' }
    @{ Path = $sqliteInRelease; Name = 'System.Data.SQLite.dll' }
    @{ Path = $interopInRelease; Name = 'x64\SQLite.Interop.dll' }
)
$missing = $oivRequired | Where-Object { -not (Test-Path $_.Path) } | ForEach-Object { $_.Name }
if ($missing) {
    Write-Error @"
Cannot create OpenIV package: the following required files are missing from Release:
  $($missing -join "`n  ")

SQL features will not work without System.Data.SQLite.dll and x64\SQLite.Interop.dll.
Ensure you have run 'dotnet restore' and that Dependencies\ contains:
  - System.Data.SQLite.dll
  - x64\SQLite.Interop.dll

Or run from a clean clone: dotnet restore MDTProPlugin\MDTPro.sln ; .\build.ps1
"@
    exit 1
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

    # Content structure for OIV - use Release folder as single source of truth
    # (ensures OIV contains exactly what we validated and deployed)
    $pluginsOiv = Join-Path $oivContent 'plugins\LSPDFR'
    New-Item -ItemType Directory -Path $pluginsOiv -Force | Out-Null
    Copy-Item -Path $dllDest -Destination (Join-Path $pluginsOiv 'MDTPro.dll') -Force
    Copy-Item -Path $newtonsoftInRelease -Destination (Join-Path $pluginsOiv 'Newtonsoft.Json.dll') -Force
    Copy-Item -Path $sqliteInRelease -Destination (Join-Path $oivContent 'System.Data.SQLite.dll') -Force
    $x64Oiv = Join-Path $oivContent 'x64'
    New-Item -ItemType Directory -Path $x64Oiv -Force | Out-Null
    Copy-Item -Path $interopInRelease -Destination (Join-Path $x64Oiv 'SQLite.Interop.dll') -Force
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
MDT Pro — install & requirements
=================================

MDT Pro runs a small web server while you are on duty so you can use the MDT in a browser (same PC or
another device on your network).


REQUIREMENTS (install these before MDT Pro)
--------------------------------------------
  • LSPDFR and RagePluginHook
  • Common Data Framework (CDF) — REQUIRED for every setup. The plugin will not load without it.
    You still need CDF if you use StopThePed and Ultimate Backup instead of Policing Redefined.
  • CalloutInterfaceAPI (GTA V root or plugins\LSPDFR\)
  • CalloutInterface — required for the Active Call page (live callout details)

  Stops, citations, and backup — pick ONE integration path (do not mix):

  A) Policing Redefined
     Install Policing Redefined. In the MDT: Customization → Config → Mod integration, choose settings
     that match PR (stops, citations, backup via PR or Auto when PR is present).

  B) StopThePed + Ultimate Backup
     Install StopThePed and Ultimate Backup. In the MDT: Customization → Config → Mod integration, set
     stop/traffic integration to StopThePed and backup to Ultimate Backup (or Auto if only UB is present).

     IMPORTANT: If you use StopThePed + Ultimate Backup, do NOT install Policing Redefined. Running PR
     together with that stack is unsupported and can cause broken or duplicate behavior. CDF is still
     required.


OPENIV INSTALL (recommended)
-----------------------------
  • MDTPro-*.oiv          — Install package. OpenIV: Edit mode, drag the .oiv onto OpenIV, or
                           Tools → Package Installer, then install into the game folder.
  • MDTPro-*-Uninstall.oiv — Removes MDT Pro via OpenIV. For a full cleanup you may still delete the
                           MDTPro folder manually if needed.


MANUAL INSTALL (no OpenIV)
--------------------------
Copy into your GTA V folder (the folder that contains GTA5.exe). Do NOT copy the .oiv files into the game.

Merge these paths:

  From this release                    Into your GTA V folder
  -------------------------            ------------------------
  plugins\lspdfr\MDTPro.dll            plugins\LSPDFR\MDTPro.dll
  plugins\lspdfr\Newtonsoft.Json.dll   plugins\LSPDFR\Newtonsoft.Json.dll
  MDTPro\  (entire folder)             MDTPro\
  System.Data.SQLite.dll               (GTA V root, next to GTA5.exe)
  x64\SQLite.Interop.dll               x64\SQLite.Interop.dll

SQLite: System.Data.SQLite.dll MUST be in the game root (not only under plugins). The native loader uses
the game directory. If the database fails to open, check both that DLL and x64\SQLite.Interop.dll.


FIRST RUN & USE
---------------
  1. Start the game, go on duty with LSPDFR.
  2. MDT Pro shows notification(s) with addresses such as http://127.0.0.1:9000 (default port 9000).
  3. Open that URL in a browser (Chrome / Brave recommended). Addresses are also in MDTPro\ipAddresses.txt.

  Another device (phone, tablet, PC) cannot connect: usually Windows Firewall on the game PC — add an
  inbound rule for the port you use (9000 by default), or change the port under Customization → Config.


UPDATING
--------
  Overwrite the plugin DLLs, MDTPro folder, and SQLite files with the new release. Your existing
  MDTPro\data\ and MDTPro\config.json are kept if you do not delete the MDTPro folder outright.


TROUBLESHOOTING
---------------
  • Log file: MDTPro\MDTPro.log (next to config.json)
  • Plugin load errors: also check RAGEPluginHook.log in the GTA V folder


Credits
-------
MDT Pro is derived from External Police Computer (EPC), an LSPDFR police computer mod.
Thanks to the EPC authors and contributors for the original project and idea.

License
-------
MDT Pro is licensed under the Eclipse Public License 2.0 (EPL-2.0). Full text: LICENSE in this folder.
Some default data files may be under the MIT License; see the project repository for details.
"@ | Out-File -FilePath $readmePath -Encoding UTF8
Write-Host "  -> $readmePath (install instructions)"

# EPL-2.0: include license text with release distributions
$licenseSrc = Join-Path $root 'LICENSE'
$licenseDest = Join-Path $release 'LICENSE'
if (Test-Path $licenseSrc) {
    Copy-Item -Path $licenseSrc -Destination $licenseDest -Force
    Write-Host "  -> $licenseDest (EPL-2.0)"
}

# 9) Mirror Release → Native Release (distribution folder) + Windows MDC app
Write-Host "Staging Native Release\ (mirror of Release + native desktop)..."
if (Test-Path $legacyReleasesDir) {
    Write-Host "  Removing legacy Releases\ (output is now Native Release\ only)..."
    Remove-Item -Path $legacyReleasesDir -Recurse -Force
}
if (Test-Path $nativeReleaseDir) {
    Remove-Item -Path $nativeReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $nativeReleaseDir -Force | Out-Null
Get-ChildItem -Path $release -Force | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $nativeReleaseDir $_.Name) -Recurse -Force
}
Write-Host "  -> $nativeReleaseDir\ (game mod + OpenIV packages + README)"

if (-not $SkipWindowsApp) {
    if (-not (Test-Path $nativeWpfProj)) {
        Write-Error "Native WPF project not found: $nativeWpfProj"
        exit 1
    }
    Write-Host "Publishing MDT Pro Native (Windows MDC)..."
    if (Test-Path $nativePublishDir) {
        Remove-Item -Path $nativePublishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $nativePublishDir -Force | Out-Null
    $publishArgs = @(
        'publish', $nativeWpfProj,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-o', $nativePublishDir,
        '-v', 'minimal'
    )
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "Native WPF publish failed."; exit $LASTEXITCODE }
    Write-Host "  -> $nativePublishDir (win-x64, self-contained)"

    $nativeReadme = Join-Path $nativePublishDir 'README.txt'
    @"
MDT Pro — Native MDC (Windows desktop)
========================================

This folder is the published Windows desktop MDC terminal. It talks to the same HTTP/WebSocket
API as the in-game browser MDT (your LSPDFR plugin must be running and on duty).

  1. Start GTA V, go on duty; note the MDT URL/port (default http://127.0.0.1:9000).
  2. Run MDTProNative.Wpf.exe.
  3. Enter the same host and port, then Connect.

Requires the WebView2 Runtime for Settings → embedded customization page (usually already installed with Edge). Reports and most modules are native WPF.

The game mod, OpenIV packages, and full install notes are in the parent Native Release\ folder next to this folder.

License: EPL-2.0 — see LICENSE in Native Release\.
"@ | Out-File -FilePath $nativeReadme -Encoding UTF8
    Write-Host "  -> $nativeReadme"
}
else {
    Write-Host "  (skipped native desktop: -SkipWindowsApp)"
}

Write-Host "Done. Full mod release: $release"
Write-Host "  Release\plugins\lspdfr\MDTPro.dll"
Write-Host "  Release\System.Data.SQLite.dll (GTA V root)"
Write-Host "  Release\x64\SQLite.Interop.dll (GTA V root\x64)"
Write-Host "  Release\MDTPro\"
Write-Host "Distribution copy (mod + desktop): $nativeReleaseDir"
if (-not $SkipWindowsApp) {
    Write-Host "  Native Release\MDTProNative\MDTProNative.Wpf.exe"
}

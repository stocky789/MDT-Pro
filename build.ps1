# MDT Pro — builds the full mod into Release\ (plugin DLL + MDTPro web + native desktop + dependencies),
# publishes the Windows MDC app into Release\MDTProNative (so OpenIV installs it to the GTA V root),
# then mirrors Release\ into Native Release\.
# Run from repo root: .\build.ps1
#
# Options:
#   .\build.ps1                    Clean rebuild (wipes Release, full build; Native Release\ rebuilt at end)
#   .\build.ps1 -Incremental       Faster build (no wipe, incremental)
#   .\build.ps1 -SkipWindowsApp    Build mod only; no MDTProNative\ in Release/OIV (still mirrors Release → Native Release)
#   .\build.ps1 -Deploy            Build then copy to GTA V (use -GamePath if not default)
#   .\build.ps1 -Deploy -GamePath "D:\Games\GTA V"
#   GamePath is also used to copy DamageTrackingFramework.dll into Release (required for injury reports; or use References\DamageTrackingFramework.dll).
#   OpenIV packages (install + uninstall) are always created in Release\ (and copied to Native Release\).
#
# Native Release\ layout:
#   Mirror of Release\ (game mod, MDTPro\, MDTProNative\, .oiv, README, LICENSE).

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
$nativePublishInRelease = Join-Path $release 'MDTProNative'
$dllDestDir = Join-Path $release 'plugins\lspdfr'
$dllDest = Join-Path $dllDestDir 'MDTPro.dll'
$mdtSource = Join-Path $root 'MDTPro'
$mdtDest = Join-Path $release 'MDTPro'

function Get-DotNetProjectAssemblyName {
    param([Parameter(Mandatory)][string]$ProjectPath)
    $m = Select-String -Path $ProjectPath -Pattern '<AssemblyName>\s*([^<]+?)\s*</AssemblyName>' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($m) { return $m.Matches.Groups[1].Value.Trim() }
    return [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
}

# Out-File -Encoding UTF8 uses no BOM on Windows PowerShell 5.1; Notepad then reads as ANSI and mangles bullets (â€¢). BOM makes UTF-8 explicit.
function Write-TextFileUtf8Bom {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )
    $parent = Split-Path -Parent -Path $Path
    if (-not [string]::IsNullOrEmpty($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $enc = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

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

# 6c) MDT Pro Native (Windows MDC) — publish into Release\MDTProNative so OpenIV / manual install match GTA V layout (folder next to MDTPro\).
$nativeAppExeName = $null
if (-not $SkipWindowsApp) {
    if (-not (Test-Path $nativeWpfProj)) {
        Write-Error "Native WPF project not found: $nativeWpfProj"
        exit 1
    }
    Write-Host "Publishing MDT Pro Native into Release\MDTProNative..."
    if (Test-Path $nativePublishInRelease) {
        Remove-Item -Path $nativePublishInRelease -Recurse -Force
    }
    New-Item -ItemType Directory -Path $nativePublishInRelease -Force | Out-Null
    $publishArgs = @(
        'publish', $nativeWpfProj,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-o', $nativePublishInRelease,
        '-v', 'minimal'
    )
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "Native WPF publish failed."; exit $LASTEXITCODE }
    Write-Host "  -> $nativePublishInRelease (win-x64, self-contained)"

    $nativeAppExeName = "$(Get-DotNetProjectAssemblyName $nativeWpfProj).exe"
    $nativeAppExePath = Join-Path $nativePublishInRelease $nativeAppExeName
    if (-not (Test-Path -LiteralPath $nativeAppExePath)) {
        $firstExe = Get-ChildItem -Path $nativePublishInRelease -Filter '*.exe' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike 'createdump.exe' } |
            Select-Object -First 1
        if ($firstExe) { $nativeAppExeName = $firstExe.Name }
    }

    $nativeReadme = Join-Path $nativePublishInRelease 'README.txt'
    $nativeReadmeBody = @"
MDT Pro — Native MDC (Windows desktop)
========================================

This folder is the published Windows desktop MDC terminal. It talks to the same HTTP/WebSocket
API as the in-game browser MDT (your LSPDFR plugin must be running and on duty).

  1. Start GTA V, go on duty; note the MDT URL/port (default http://127.0.0.1:9000).
  2. Run $nativeAppExeName (from this folder).
  3. Enter the same host and port, then Connect.

When installed via OpenIV, this folder lives next to MDTPro\ in your GTA V directory.

Requires the WebView2 Runtime for Settings → embedded customization page (usually already installed with Edge). Reports and most modules are native WPF.

The game mod, OpenIV packages, and full install notes are in the parent release folder.

License: EPL-2.0 — see LICENSE next to README in the release.
"@
    Write-TextFileUtf8Bom -Path $nativeReadme -Content $nativeReadmeBody
    Write-Host "  -> $nativeReadme"
} else {
    if (Test-Path $nativePublishInRelease) {
        Remove-Item -Path $nativePublishInRelease -Recurse -Force
        Write-Host "  Removed Release\MDTProNative (skipped native desktop: -SkipWindowsApp)"
    } else {
        Write-Host "  (skipped native desktop: -SkipWindowsApp)"
    }
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

    $nativeSrc = Join-Path $releaseRoot 'MDTProNative'
    if (Test-Path -LiteralPath $nativeSrc) {
        $nativeGameDest = Join-Path $GamePath 'MDTProNative'
        if (Test-Path $nativeGameDest) { Remove-Item $nativeGameDest -Recurse -Force }
        Copy-Item -Path $nativeSrc -Destination $nativeGameDest -Recurse -Force
        Write-Host "  -> $nativeGameDest (MDT Pro Native)"
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

    # Content = full game payload from Release (same layout as manual install / -Deploy).
    # Excludes distribution-only files so OpenIV drops match the release folder.
    function Test-ReleaseItemExcludedFromOiv {
        param([string]$Name)
        if ($Name -eq 'README.txt' -or $Name -eq 'LICENSE') { return $true }
        if ($Name -match '\.oiv$') { return $true }
        if ($Name -eq 'oivBuild' -or $Name -eq 'oivUninstall') { return $true }
        return $false
    }

    Get-ChildItem -LiteralPath $releaseRoot -Force | ForEach-Object {
        if (Test-ReleaseItemExcludedFromOiv $_.Name) { return }
        $dest = Join-Path $oivContent $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
        }
    }

    # LSPDFR expects plugins\LSPDFR (caps). Build output uses plugins\lspdfr — normalize for the package.
    # NTFS is case-insensitive: renaming lspdfr -> LSPDFR in one step is a no-op; use a temp name.
    $oivPlugins = Join-Path $oivContent 'plugins'
    if (Test-Path -LiteralPath $oivPlugins) {
        $low = Join-Path $oivPlugins 'lspdfr'
        $hi = Join-Path $oivPlugins 'LSPDFR'
        if (Test-Path -LiteralPath $low) {
            $actual = (Get-Item -LiteralPath $low).Name
            if ($actual -cne 'LSPDFR') {
                $tmp = Join-Path $oivPlugins '_mdt_oiv_lspdfr_tmp'
                if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
                Rename-Item -LiteralPath $low -NewName '_mdt_oiv_lspdfr_tmp'
                Rename-Item -LiteralPath $tmp -NewName 'LSPDFR'
            }
        }
    }

    # assembly.xml <add> for every file under content/ (paths relative to GTA V folder)
    $allAddLines = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem -LiteralPath $oivContent -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($oivContent.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
        if ([string]::IsNullOrEmpty($rel)) { return }
        $allAddLines.Add("    <add source=`"$rel`">$rel</add>")
    }
    $contentXml = $allAddLines -join "`n"

    # Uninstaller: remove everything under MDTPro/ from the mirrored tree, plus MDT Pro plugin files only.
    $mdtDeletes = [System.Collections.Generic.List[string]]::new()
    $mdtFolderOiv = Join-Path $oivContent 'MDTPro'
    if (Test-Path -LiteralPath $mdtFolderOiv) {
        Get-ChildItem -LiteralPath $mdtFolderOiv -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($oivContent.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            if (-not [string]::IsNullOrEmpty($rel)) { $mdtDeletes.Add("    <delete>$rel</delete>") }
        }
    }

    $pluginMdtDeletes = [System.Collections.Generic.List[string]]::new()
    $pluginsLspdfrOiv = Join-Path $oivContent 'plugins\LSPDFR'
    if (Test-Path -LiteralPath $pluginsLspdfrOiv) {
        Get-ChildItem -LiteralPath $pluginsLspdfrOiv -File | Where-Object { $_.Name -like 'MDTPro*' } | ForEach-Object {
            $pluginMdtDeletes.Add("    <delete>plugins/LSPDFR/$($_.Name)</delete>")
        }
    }
    if ($pluginMdtDeletes.Count -eq 0) {
        $pluginMdtDeletes.Add('    <delete>plugins/LSPDFR/MDTPro.dll</delete>')
    }

    $nativeDeletes = [System.Collections.Generic.List[string]]::new()
    $nativeFolderOiv = Join-Path $oivContent 'MDTProNative'
    if (Test-Path -LiteralPath $nativeFolderOiv) {
        Get-ChildItem -LiteralPath $nativeFolderOiv -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($oivContent.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            if (-not [string]::IsNullOrEmpty($rel)) { $nativeDeletes.Add("    <delete>$rel</delete>") }
        }
    }

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
    Write-Host "  -> $oivOut (OpenIV package, $($allAddLines.Count) files mapped to game folder paths)"

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
$($pluginMdtDeletes -join "`n")
$($mdtDeletes -join "`n")
$($nativeDeletes -join "`n")
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
      <![CDATA[Removes MDT Pro (plugin DLL, MDTPro web folder, and MDTProNative desktop app folder). Leaves shared dependencies (SQLite, Newtonsoft.Json, DamageTrackingFramework) so other mods keep working. For a full purge, manually delete leftover folders and any dependencies if no other mod needs them.]]>
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
$releaseReadmeBody = @"
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
  • MDTPro-*.oiv          — Installs the same folder layout as this release (everything below except
                           README.txt, LICENSE, and the .oiv files). Paths match the GTA V directory
                           (plugins\LSPDFR, MDTPro\, MDTProNative\, game root DLLs, x64\, etc.).
  • MDTPro-*-Uninstall.oiv — Removes MDT Pro plugin files, the MDTPro web folder, and MDTProNative\;
                           leaves SQLite and Newtonsoft.Json so other mods can keep using them. Extra
                           DLLs you merged into plugins\LSPDFR are not removed by the uninstaller.


MANUAL INSTALL (no OpenIV)
--------------------------
Copy into your GTA V folder (the folder that contains GTA5.exe). Do NOT copy the .oiv files into the game.

Copy everything from this release except README.txt, LICENSE, and *.oiv — preserve paths. Typical layout:

  From this release                    Into your GTA V folder
  -------------------------            ------------------------
  plugins\lspdfr\  (all files)         plugins\LSPDFR\  (merge; LSPDFR is the usual folder name)
  MDTPro\  (entire folder)             MDTPro\
  MDTProNative\  (entire folder)       MDTProNative\  (Windows desktop MDC; omitted if you used -SkipWindowsApp)
  System.Data.SQLite.dll               (GTA V root, next to GTA5.exe)
  Newtonsoft.Json.dll (if at release   (GTA V root, only if present in your release zip)
   root)
  x64\SQLite.Interop.dll               x64\SQLite.Interop.dll

SQLite: System.Data.SQLite.dll MUST be in the game root (not only under plugins). The native loader uses
the game directory. If the database fails to open, check both that DLL and x64\SQLite.Interop.dll.


FIRST RUN & USE
---------------
  1. Start the game, go on duty with LSPDFR.
  2. MDT Pro shows notification(s) with addresses such as http://127.0.0.1:9000 (default port 9000).
  3. Open that URL in a browser (Chrome / Brave recommended). Addresses are also in MDTPro\ipAddresses.txt.
     Or run MDTProNative\MDTPro.exe (desktop MDC) and connect to the same host/port.

  Another device (phone, tablet, PC) cannot connect: usually Windows Firewall on the game PC — add an
  inbound rule for the port you use (9000 by default), or change the port under Customization → Config.


UPDATING
--------
  Overwrite the plugin DLLs, MDTPro, MDTProNative, and SQLite files with the new release. Your existing
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
"@
Write-TextFileUtf8Bom -Path $readmePath -Content $releaseReadmeBody
Write-Host "  -> $readmePath (install instructions)"

# EPL-2.0: include license text with release distributions
$licenseSrc = Join-Path $root 'LICENSE'
$licenseDest = Join-Path $release 'LICENSE'
if (Test-Path $licenseSrc) {
    Copy-Item -Path $licenseSrc -Destination $licenseDest -Force
    Write-Host "  -> $licenseDest (EPL-2.0)"
}

# 9) Mirror Release → Native Release (full distribution folder; MDTProNative already built in Release\)
Write-Host "Staging Native Release\ (mirror of Release\)..."
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
Write-Host "  -> $nativeReleaseDir\ (mirror of Release\, includes MDTProNative when built)"

Write-Host "Done. Full mod release: $release"
Write-Host "  Release\plugins\lspdfr\MDTPro.dll"
Write-Host "  Release\System.Data.SQLite.dll (GTA V root)"
Write-Host "  Release\x64\SQLite.Interop.dll (GTA V root\x64)"
Write-Host "  Release\MDTPro\"
if (-not $SkipWindowsApp -and $nativeAppExeName) {
    Write-Host "  Release\MDTProNative\$nativeAppExeName"
}
Write-Host "Distribution copy (mod + desktop): $nativeReleaseDir"
if (-not $SkipWindowsApp -and $nativeAppExeName) {
    Write-Host "  Native Release\MDTProNative\$nativeAppExeName"
}

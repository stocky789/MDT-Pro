# Rasterize MDTPro/img/favicon.png to MdtProApp.ico (256px) for Windows ApplicationIcon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $root '..\..\..')
$srcPath = Join-Path $repoRoot 'MDTPro\img\favicon.png'
$outPath = Join-Path $PSScriptRoot 'MdtProApp.ico'
if (-not (Test-Path $srcPath)) { throw "Missing $srcPath" }

$size = 256
$src = $null
$bmp = $null
$g = $null
$icon = $null
$fs = $null
try {
    $src = [System.Drawing.Image]::FromFile($srcPath)
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($src, 0, 0, $size, $size)
    } finally {
        if ($null -ne $g) {
            $g.Dispose()
            $g = $null
        }
    }

    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $fs = [System.IO.File]::Create($outPath)
    try {
        $icon.Save($fs)
    } finally {
        if ($null -ne $fs) {
            $fs.Dispose()
            $fs = $null
        }
    }
} finally {
    if ($null -ne $g) { $g.Dispose() }
    if ($null -ne $icon) { $icon.Dispose() }
    if ($null -ne $bmp) { $bmp.Dispose() }
    if ($null -ne $src) { $src.Dispose() }
}
Write-Host "Wrote $outPath"

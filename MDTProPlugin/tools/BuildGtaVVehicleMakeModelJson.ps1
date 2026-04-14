$ErrorActionPreference = 'Stop'
$uri = 'https://raw.githubusercontent.com/DurtyFree/gta-v-data-dumps/master/vehicles.json'
$tmp = Join-Path $env:TEMP 'gta-v-vehicles-dump.json'
Invoke-WebRequest -Uri $uri -OutFile $tmp -UseBasicParsing
$src = $tmp
$arr = Get-Content $src -Raw -Encoding UTF8 | ConvertFrom-Json
$out = @{}
foreach ($item in $arr) {
    $n = $item.Name
    if ([string]::IsNullOrWhiteSpace($n)) { continue }
    $mk = $item.ManufacturerDisplayName.English
    $md = $item.DisplayName.English
    if ([string]::IsNullOrWhiteSpace($mk) -and [string]::IsNullOrWhiteSpace($md)) { continue }
    $key = $n.ToLowerInvariant()
    if (-not $out.ContainsKey($key)) {
        $out[$key] = @{ Make = $mk; Model = $md }
    }
}
$dest = Join-Path $PSScriptRoot '..\MDTPro\Data\GtaVVehicleMakeModel.json'
$json = $out | ConvertTo-Json -Depth 5 -Compress
[System.IO.File]::WriteAllText($dest, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($out.Count) entries to $dest"

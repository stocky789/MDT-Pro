# Compares STPEvents subscription list to StopThePed.API.Events on STP/StopThePed.dll (reflection-only).
# Exit 0 = match, 1 = mismatch or missing files.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root 'STP\StopThePed.dll'
$stEvents = Join-Path $root 'MDTProPlugin\MDTPro\EventListeners\STPEvents.cs'
if (-not (Test-Path $dll)) { Write-Error "Missing $dll"; exit 1 }
if (-not (Test-Path $stEvents)) { Write-Error "Missing $stEvents"; exit 1 }

$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path $dll).Path)
$evType = $asm.GetType('StopThePed.API.Events')
if (-not $evType) { Write-Error 'StopThePed.API.Events not found in DLL'; exit 1 }
$fromDll = @($evType.GetEvents([System.Reflection.BindingFlags]'Public,Static') | ForEach-Object { $_.Name })

$text = [System.IO.File]::ReadAllText($stEvents)
$fromCs = [System.Collections.Generic.List[string]]::new()
$inArray = $false
foreach ($line in $text -split "`n") {
    if ($line -match 'subscriptionEventNames\s*=') { $inArray = $true; continue }
    if ($inArray -and $line -match '^\s*};') { break }
    if ($inArray -and $line -match '"([a-zA-Z0-9_]+)"') { $fromCs.Add($Matches[1]) }
}

$dups = $fromCs | Group-Object | Where-Object { $_.Count -gt 1 }
if ($dups) {
    Write-Host "FAIL: duplicate event name(s) in STPEvents.cs: $(($dups | ForEach-Object { $_.Name }) -join ', ')"
    exit 1
}

$missingInCs = $fromDll | Where-Object { $_ -notin $fromCs }
$extraInCs = $fromCs | Where-Object { $_ -notin $fromDll }
if ($missingInCs.Count -gt 0 -or $extraInCs.Count -gt 0) {
    Write-Host 'FAIL: event name set mismatch.'
    if ($missingInCs.Count -gt 0) { Write-Host "  In DLL but not STPEvents.cs: $($missingInCs -join ', ')" }
    if ($extraInCs.Count -gt 0) { Write-Host "  In STPEvents.cs but not DLL: $($extraInCs -join ', ')" }
    exit 1
}
if ($fromDll.Count -ne $fromCs.Count) {
    Write-Host "FAIL: duplicate or count mismatch (DLL unique $($fromDll.Count), CS entries $($fromCs.Count))."
    exit 1
}
Write-Host "OK: STPEvents subscriptionEventNames matches StopThePed.API.Events ($($fromDll.Count) events)."
exit 0

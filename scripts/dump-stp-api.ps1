$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '..\STP\StopThePed.dll' | Resolve-Path
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path.Path)

function Show-Methods($t) {
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]'Public,Static')) {
        if ($m.IsSpecialName) { continue }
        try {
            $parms = $m.GetParameters()
        } catch {
            Write-Output "? $($m.Name)(unresolved-params)"
            continue
        }
        $pn = @()
        foreach ($p in $parms) {
            try { $pn += $p.ParameterType.Name } catch { $pn += '?' }
        }
        $ps = $pn -join ', '
        try { $ret = $m.ReturnType.Name } catch { $ret = '?' }
        "$ret $($m.Name)($ps)"
    }
}

foreach ($t in $asm.GetExportedTypes() | Sort-Object FullName) {
    if ($t.FullName -notlike 'StopThePed.API*') { continue }
    Write-Output "--- $($t.FullName) ---"
    if ($t.IsEnum) {
        $t.GetEnumNames() | ForEach-Object { Write-Output "  $_" }
        continue
    }
    if ($t.IsClass -or $t.IsInterface) {
        Show-Methods $t
        foreach ($ev in $t.GetEvents([System.Reflection.BindingFlags]'Public,Static')) {
            $ht = '?'
            try { $ht = $ev.EventHandlerType.Name } catch { }
            Write-Output "  event $($ev.Name) -> $ht"
        }
    }
}

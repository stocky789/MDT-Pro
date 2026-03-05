# MDT Pro - Release build (wipe Release folder, build plugin, copy web MDT).
# Output: Release\ is a fresh, complete mod ready to zip for the LSPDFR forum.
Set-Location $PSScriptRoot
dotnet build MDTProPlugin\MDTPro.sln -c Release
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Release folder: $PSScriptRoot\Release" -ForegroundColor Green
    Write-Host "  - Plugin: Release\plugins\lspdfr\MDTPro.dll"
    Write-Host "  - Web MDT: Release\MDTPro\"
    Write-Host "Ready to zip and upload." -ForegroundColor Green
}

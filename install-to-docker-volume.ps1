# Install Plugin to Docker Volume Mount
param(
    [string]$PluginDir = "D:\media-stack\jellyfin\server\plugins\Migrator_1.0.0.0"
)

Write-Host "Installing Migrator plugin to volume mount..." -ForegroundColor Cyan
Write-Host ""

# Build
Write-Host "[1/3] Building plugin..." -ForegroundColor Green
& ./build.ps1 | Out-Null

# Copy to host volume
Write-Host "[2/3] Copying to volume mount..." -ForegroundColor Green
if (-not (Test-Path $PluginDir)) {
    New-Item -ItemType Directory -Path $PluginDir -Force | Out-Null
}

# Clean and copy only required files
Remove-Item "$PluginDir\*" -Force -ErrorAction SilentlyContinue
Copy-Item "./publish/Jellyfin.Plugin.Template.dll" -Destination $PluginDir
Copy-Item "./publish/Microsoft.Data.Sqlite.dll" -Destination $PluginDir
Copy-Item "./publish/SQLitePCLRaw.batteries_v2.dll" -Destination $PluginDir
Copy-Item "./publish/SQLitePCLRaw.core.dll" -Destination $PluginDir
Copy-Item "./publish/SQLitePCLRaw.provider.e_sqlite3.dll" -Destination $PluginDir

Write-Host "Files copied:"
Get-ChildItem $PluginDir | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}

# Restart container
Write-Host ""
Write-Host "[3/3] Restarting Jellyfin..." -ForegroundColor Green
docker restart jellyfin | Out-Null

Write-Host ""
Write-Host "Done! Wait 30 seconds then check:" -ForegroundColor Cyan
Write-Host "  http://localhost:8096/web/index.html#!/dashboard/plugins" -ForegroundColor Yellow
Write-Host ""
Write-Host "IMPORTANT: Your docker-compose.yml should have:" -ForegroundColor Yellow
Write-Host '  "D:/media-stack/jellyfin/server/plugins:/config/plugins"' -ForegroundColor White
Write-Host "  (NOT /var/lib/jellyfin/plugins)" -ForegroundColor Gray

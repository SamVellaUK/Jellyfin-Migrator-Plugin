# Simple Docker Plugin Installer
param(
    [string]$ContainerName = "jellyfin"
)

$ErrorActionPreference = 'Stop'

Write-Host "Building plugin..." -ForegroundColor Green
& ./build.ps1 | Out-Null

Write-Host "Ensuring container is running..." -ForegroundColor Green
docker start $ContainerName 2>&1 | Out-Null
Start-Sleep -Seconds 5

Write-Host "Creating plugin directory..." -ForegroundColor Green
$path = "/config/data/plugins/Migrator_1.0.0.0"
docker exec $ContainerName mkdir -p $path 2>&1 | Out-Null

Write-Host "Copying files..." -ForegroundColor Green
docker cp ./publish/Jellyfin.Plugin.Template.dll "${ContainerName}:${path}/"
docker cp ./publish/Microsoft.Data.Sqlite.dll "${ContainerName}:${path}/"
docker cp ./publish/SQLitePCLRaw.batteries_v2.dll "${ContainerName}:${path}/"
docker cp ./publish/SQLitePCLRaw.core.dll "${ContainerName}:${path}/"
docker cp ./publish/SQLitePCLRaw.provider.e_sqlite3.dll "${ContainerName}:${path}/"

Write-Host "Fixing permissions..." -ForegroundColor Green
docker exec $ContainerName chown -R abc:abc $path

Write-Host "Restarting container..." -ForegroundColor Green
docker restart $ContainerName | Out-Null

Write-Host ""
Write-Host "Done! Wait 30 seconds then check:" -ForegroundColor Cyan
Write-Host "  http://localhost:8096/web/index.html#!/dashboard/plugins" -ForegroundColor Yellow
Write-Host ""
Write-Host "To verify files:" -ForegroundColor Cyan
Write-Host "  docker exec $ContainerName ls -la $path" -ForegroundColor Gray

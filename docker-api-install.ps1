#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Force Jellyfin to load plugin via API refresh

.DESCRIPTION
    Uses Jellyfin's API to trigger a plugin rescan after files are copied to the container.
    This works around the LinuxServer.io Docker image's plugin loading issues.

.EXAMPLE
    ./docker-api-install.ps1
    ./docker-api-install.ps1 -Server "http://localhost:8096" -ApiKey "your-key"
#>

param(
    [string]$Server = "http://localhost:8096",
    [string]$ApiKey = "",
    [string]$ContainerName = "jellyfin"
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Jellyfin Docker Plugin Installer (API Method) ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build and copy files
Write-Host "[1/4] Building plugin..." -ForegroundColor Green
try {
    & ./build.ps1 -ErrorAction Stop | Out-Null
    Write-Host "✓ Build complete" -ForegroundColor Green
} catch {
    Write-Error "Build failed: $_"
    exit 1
}

Write-Host ""
Write-Host "[2/4] Copying files to Docker container..." -ForegroundColor Green

# Stop container first (clean slate)
Write-Host "  Stopping container..." -ForegroundColor Gray
docker stop $ContainerName | Out-Null

# Copy files to the correct location
$pluginPath = "/config/data/plugins/Migrator_1.0.0.0"
docker exec $ContainerName mkdir -p $pluginPath 2>&1 | Out-Null

Write-Host "  Copying DLLs..." -ForegroundColor Gray
$files = @(
    "Jellyfin.Plugin.Template.dll",
    "Microsoft.Data.Sqlite.dll",
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll"
)

foreach ($file in $files) {
    docker cp "./publish/$file" "${ContainerName}:${pluginPath}/" | Out-Null
}

Write-Host "✓ Files copied" -ForegroundColor Green

# Step 2: Start container
Write-Host ""
Write-Host "[3/4] Starting container..." -ForegroundColor Green
docker start $ContainerName | Out-Null
Start-Sleep -Seconds 10  # Wait for Jellyfin to start
Write-Host "✓ Container started" -ForegroundColor Green

# Step 3: Get API key if not provided
if ([string]::IsNullOrEmpty($ApiKey)) {
    Write-Host ""
    Write-Host "[4/4] API Key Required" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To complete installation, you need an API key:" -ForegroundColor Yellow
    Write-Host "  1. Open Jellyfin: $Server" -ForegroundColor Cyan
    Write-Host "  2. Login and go to: Dashboard > API Keys" -ForegroundColor Cyan
    Write-Host "  3. Create a new API key" -ForegroundColor Cyan
    Write-Host ""
    $ApiKey = Read-Host "Enter your API key (or press Enter to skip)"
}

if (-not [string]::IsNullOrEmpty($ApiKey)) {
    Write-Host ""
    Write-Host "[4/4] Triggering plugin refresh via API..." -ForegroundColor Green

    $Headers = @{
        "X-Emby-Token" = $ApiKey
    }

    try {
        # Method 1: Restart via API
        Write-Host "  Attempting API restart..." -ForegroundColor Gray
        Invoke-RestMethod -Uri "$Server/System/Restart" -Headers $Headers -Method Post -ErrorAction SilentlyContinue | Out-Null
        Write-Host "✓ Restart triggered via API" -ForegroundColor Green
        Write-Host "  Waiting for Jellyfin to restart..." -ForegroundColor Gray
        Start-Sleep -Seconds 15
    } catch {
        Write-Host "  API restart not available, using docker restart..." -ForegroundColor Gray
        docker restart $ContainerName | Out-Null
        Start-Sleep -Seconds 15
    }

    # Check if plugin loaded
    try {
        $plugins = Invoke-RestMethod -Uri "$Server/Plugins" -Headers $Headers -Method Get
        $migrator = $plugins | Where-Object { $_.Name -eq "Migrator" }

        if ($migrator) {
            Write-Host ""
            Write-Host "✓✓✓ SUCCESS! Plugin loaded successfully! ✓✓✓" -ForegroundColor Green
            Write-Host ""
            Write-Host "Plugin Details:" -ForegroundColor Cyan
            Write-Host "  Name:    $($migrator.Name)" -ForegroundColor Gray
            Write-Host "  Version: $($migrator.Version)" -ForegroundColor Gray
            Write-Host "  Status:  $($migrator.Status)" -ForegroundColor Gray
        } else {
            Write-Warning "Plugin files copied but not loaded yet."
            Write-Host ""
            Write-Host "Installed plugins:" -ForegroundColor Cyan
            $plugins | ForEach-Object { Write-Host "  - $($_.Name) $($_.Version)" -ForegroundColor Gray }
        }
    } catch {
        Write-Warning "Could not verify plugin status: $_"
    }
} else {
    Write-Host ""
    Write-Host "Manual restart required:" -ForegroundColor Yellow
    Write-Host "  docker restart $ContainerName" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Then check: $Server/web/index.html#!/dashboard/plugins" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Plugin files are in:" -ForegroundColor Cyan
Write-Host "  $pluginPath" -ForegroundColor Gray
Write-Host ""
Write-Host "To verify:" -ForegroundColor Cyan
Write-Host "  docker exec $ContainerName ls -la $pluginPath" -ForegroundColor Gray

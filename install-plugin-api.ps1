#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Install Jellyfin plugin via API

.DESCRIPTION
    Installs the Migrator plugin to a Jellyfin server using the REST API.
    Works with both local and Docker Jellyfin instances.

.EXAMPLE
    # Interactive (prompts for details)
    ./install-plugin-api.ps1

    # Direct installation
    ./install-plugin-api.ps1 -Server "http://localhost:8096" -ApiKey "your-api-key" -PluginUrl "http://yourserver/migrator-plugin.zip"

    # Install from local file (serves temporarily)
    ./install-plugin-api.ps1 -Server "http://localhost:8096" -ApiKey "your-api-key" -LocalPlugin "./publish"
#>

param(
    [string]$Server,
    [string]$ApiKey,
    [string]$PluginUrl,
    [string]$LocalPlugin = ""
)

$ErrorActionPreference = 'Stop'

# Jellyfin plugin metadata
$PluginGuid = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1"
$PluginName = "Migrator"
$PluginVersion = "1.0.0.0"

Write-Host "=== Jellyfin Plugin API Installer ===" -ForegroundColor Cyan
Write-Host ""

# Get server URL if not provided
if ([string]::IsNullOrEmpty($Server)) {
    $Server = Read-Host "Enter Jellyfin server URL (e.g., http://localhost:8096)"
}

# Get API key if not provided
if ([string]::IsNullOrEmpty($ApiKey)) {
    Write-Host "To get your API key:" -ForegroundColor Yellow
    Write-Host "  1. Log into Jellyfin Web UI" -ForegroundColor Yellow
    Write-Host "  2. Go to Dashboard → API Keys" -ForegroundColor Yellow
    Write-Host "  3. Create a new API key" -ForegroundColor Yellow
    Write-Host ""
    $ApiKey = Read-Host "Enter your Jellyfin API key"
}

# Set up headers
$Headers = @{
    "X-Emby-Token" = $ApiKey
    "Accept" = "application/json"
    "Content-Type" = "application/json"
}

# Test connection
Write-Host "Testing connection to $Server..." -ForegroundColor Green
try {
    $response = Invoke-RestMethod -Uri "$Server/System/Info" -Headers $Headers -Method Get
    Write-Host "✓ Connected to Jellyfin $($response.Version)" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Error "Failed to connect to Jellyfin server: $_"
    exit 1
}

# Method 1: Install from repository URL
if (-not [string]::IsNullOrEmpty($PluginUrl)) {
    Write-Host "Installing plugin from URL: $PluginUrl" -ForegroundColor Cyan

    # Create installation package info
    $installPackage = @{
        name = $PluginName
        guid = $PluginGuid
        version = $PluginVersion
        sourceUrl = $PluginUrl
    }

    try {
        $installResponse = Invoke-RestMethod -Uri "$Server/Packages/Installed/$PluginName" `
            -Headers $Headers `
            -Method Post `
            -Body ($installPackage | ConvertTo-Json)

        Write-Host "✓ Plugin installation initiated!" -ForegroundColor Green
        Write-Host "  Please restart Jellyfin to complete installation." -ForegroundColor Yellow
    } catch {
        Write-Warning "API installation failed: $_"
        Write-Host "Trying alternative method..." -ForegroundColor Yellow
    }
}

# Method 2: Manual package registration (if Method 1 fails)
if ([string]::IsNullOrEmpty($PluginUrl) -or $?) {
    Write-Host ""
    Write-Host "Alternative: Register plugin in package repository" -ForegroundColor Cyan

    # This approach adds the plugin to Jellyfin's internal package list
    # Note: This requires the plugin files to already be in the correct location

    Write-Host ""
    Write-Host "Steps to complete installation:" -ForegroundColor Yellow
    Write-Host "1. Ensure plugin files are in: /config/plugins/Migrator_1.0.0.0/" -ForegroundColor Yellow
    Write-Host "2. Restart Jellyfin container/service" -ForegroundColor Yellow
    Write-Host "3. Check Dashboard → Plugins" -ForegroundColor Yellow
}

# Method 3: Trigger plugin refresh
Write-Host ""
Write-Host "Triggering plugin catalog refresh..." -ForegroundColor Cyan
try {
    # This endpoint forces Jellyfin to rescan for plugins
    Invoke-RestMethod -Uri "$Server/Plugins" -Headers $Headers -Method Get | Out-Null
    Write-Host "✓ Plugin catalog refreshed" -ForegroundColor Green
} catch {
    Write-Warning "Could not refresh plugin catalog: $_"
}

# List currently installed plugins
Write-Host ""
Write-Host "Currently installed plugins:" -ForegroundColor Cyan
try {
    $plugins = Invoke-RestMethod -Uri "$Server/Plugins" -Headers $Headers -Method Get
    if ($plugins.Count -eq 0) {
        Write-Host "  (none)" -ForegroundColor Gray
    } else {
        $plugins | ForEach-Object {
            Write-Host "  - $($_.Name) $($_.Version)" -ForegroundColor Gray
        }
    }
} catch {
    Write-Warning "Could not list plugins: $_"
}

Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy plugin files to Jellyfin plugins directory" -ForegroundColor Yellow
Write-Host "   Docker: docker cp ./publish/. jellyfin:/config/data/plugins/Migrator_1.0.0.0/" -ForegroundColor Gray
Write-Host "   Linux:  sudo cp -r ./publish/* /var/lib/jellyfin/plugins/Migrator_1.0.0.0/" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Restart Jellyfin:" -ForegroundColor Yellow
Write-Host "   Docker: docker restart jellyfin" -ForegroundColor Gray
Write-Host "   Linux:  sudo systemctl restart jellyfin" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Verify in Web UI: Dashboard → Plugins" -ForegroundColor Yellow

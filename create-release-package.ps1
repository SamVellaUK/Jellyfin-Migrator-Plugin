#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Create Jellyfin plugin package for GitHub Release

.DESCRIPTION
    Builds the plugin and creates a ZIP package ready for GitHub Releases.
    Also generates a manifest.json for plugin repository.

.EXAMPLE
    ./create-release-package.ps1
    ./create-release-package.ps1 -Version "1.0.1"
#>

param(
    [string]$Version = "1.0.0.0",
    [string]$OutputDir = "release"
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Jellyfin Plugin Release Packager ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build
Write-Host "[1/4] Building plugin..." -ForegroundColor Green
& ./build.ps1 | Out-Null
Write-Host "✓ Build complete" -ForegroundColor Green

# Step 2: Create release directory
Write-Host ""
Write-Host "[2/4] Creating release package..." -ForegroundColor Green
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Step 3: Create ZIP package
$zipName = "jellyfin-migrator-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path ./publish/* -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "✓ Created: $zipPath" -ForegroundColor Green

# Get file size and checksum
$zipFile = Get-Item $zipPath
$hash = (Get-FileHash $zipPath -Algorithm MD5).Hash.ToLower()
$size = $zipFile.Length

Write-Host "  Size: $([math]::Round($size/1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  MD5:  $hash" -ForegroundColor Gray

# Step 4: Create manifest.json
Write-Host ""
Write-Host "[3/4] Creating plugin manifest..." -ForegroundColor Green

# NOTE: Replace these URLs after uploading to GitHub Releases!
$githubUser = "YOUR-GITHUB-USERNAME"
$githubRepo = "Jellyfin-Migrator-Plugin"
$releaseTag = "v$Version"
$downloadUrl = "https://github.com/$githubUser/$githubRepo/releases/download/$releaseTag/$zipName"

$manifest = @(
    @{
        guid = "eb5d7894-8eef-4b36-aa6f-5d124e828ce1"
        name = "Migrator"
        description = "Export user data, library permissions, passwords, and other migration data from Jellyfin servers"
        overview = "Jellyfin Migrator Plugin - Export and migrate user data between servers"
        owner = $githubUser
        category = "General"
        versions = @(
            @{
                version = $Version
                changelog = "Cross-platform release supporting Windows, Linux, and Docker installations"
                targetAbi = "10.9.0.0"
                sourceUrl = $downloadUrl
                checksum = $hash
                timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
            }
        )
    }
)

$manifestPath = Join-Path $OutputDir "manifest.json"
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8
Write-Host "✓ Created: $manifestPath" -ForegroundColor Green

# Step 5: Create instructions
Write-Host ""
Write-Host "[4/4] Creating installation instructions..." -ForegroundColor Green

$instructions = @'
# Jellyfin Migrator Plugin - Release $Version

## Files Created

- $zipName - Plugin package (upload to GitHub Release)
- manifest.json - Plugin repository manifest (upload to GitHub Release)

## Installation Steps

### Step 1: Create GitHub Release

1. Go to: https://github.com/$githubUser/$githubRepo/releases/new
2. Tag: $releaseTag
3. Title: Jellyfin Migrator Plugin $Version
4. Upload these files:
   - $zipName
   - manifest.json

### Step 2: Update manifest.json

After creating the release, the download URLs will be:
- ZIP: https://github.com/$githubUser/$githubRepo/releases/download/$releaseTag/$zipName
- Manifest: https://github.com/$githubUser/$githubRepo/releases/download/$releaseTag/manifest.json

**IMPORTANT:** Edit manifest.json and replace:
- YOUR-GITHUB-USERNAME with your actual GitHub username
- Verify the sourceUrl points to the correct GitHub Release URL

Then re-upload the updated manifest.json to the release.

### Step 3: Install in Jellyfin

#### Option A: Add Repository (Best for updates)

1. Open Jellyfin Web UI
2. Go to: Dashboard → Plugins → Repositories
3. Click "+ Add Repository"
4. Name: Migrator Plugin
5. URL: https://github.com/$githubUser/$githubRepo/releases/download/$releaseTag/manifest.json
6. Save
7. Go to Catalog tab
8. Install "Migrator"
9. Restart Jellyfin

#### Option B: Direct ZIP Install

1. Download $zipName from GitHub Release
2. Jellyfin Web UI → Dashboard → Plugins
3. Click "Upload Plugin" (if available in your version)
4. Select the ZIP file
5. Restart Jellyfin

#### Option C: Manual Installation

**Windows:**
``````powershell
# Extract ZIP to:
C:\ProgramData\Jellyfin\Server\plugins\Migrator_$Version\
# Restart Jellyfin service
``````

**Linux:**
``````bash
# Extract ZIP to:
sudo mkdir -p /var/lib/jellyfin/plugins/Migrator_$Version
sudo unzip $zipName -d /var/lib/jellyfin/plugins/Migrator_$Version/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Migrator_$Version
sudo systemctl restart jellyfin
``````

**Docker:**
``````bash
# Extract ZIP, then:
docker cp ./Migrator_$Version jellyfin:/config/plugins/
docker exec jellyfin chown -R abc:abc /config/plugins/Migrator_$Version
docker restart jellyfin
``````

## Files in Package

- Jellyfin.Plugin.Template.dll
- Microsoft.Data.Sqlite.dll
- SQLitePCLRaw.batteries_v2.dll
- SQLitePCLRaw.core.dll
- SQLitePCLRaw.provider.e_sqlite3.dll

## Compatibility

- Jellyfin: 10.9.x - 10.10.x+
- Platforms: Windows, Linux, macOS, Docker
- .NET: 8.0

## Support

For issues, visit: https://github.com/YOUR-USERNAME/Jellyfin-Migrator-Plugin/issues
'@

$readmePath = Join-Path $OutputDir "RELEASE_INSTRUCTIONS.md"
$instructions | Set-Content $readmePath -Encoding UTF8
Write-Host "✓ Created: $readmePath" -ForegroundColor Green

# Summary
Write-Host ""
Write-Host "=== Package Created Successfully ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Read: $readmePath" -ForegroundColor White
Write-Host "2. Create GitHub Release with tag: $releaseTag" -ForegroundColor White
Write-Host "3. Upload $zipName and manifest.json" -ForegroundColor White
Write-Host "4. Update manifest.json with your GitHub username" -ForegroundColor White
Write-Host "5. Share manifest URL with users" -ForegroundColor White
Write-Host ""
Write-Host "Files ready in: $OutputDir/" -ForegroundColor Cyan
Get-ChildItem $OutputDir | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}

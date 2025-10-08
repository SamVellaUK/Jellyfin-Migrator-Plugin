#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies the Jellyfin Migrator Plugin build output for cross-platform compatibility.

.DESCRIPTION
    Checks that the plugin build produces platform-agnostic assemblies without
    platform-specific runtime identifiers or dependencies.

.EXAMPLE
    ./verify-build.ps1
    ./verify-build.ps1 -PublishPath ./publish
#>

param(
    [string]$PublishPath = 'publish'
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Jellyfin Migrator Plugin - Build Verification ===" -ForegroundColor Cyan
Write-Host ""

# Check if publish directory exists
if (-not (Test-Path $PublishPath)) {
    Write-Error "Publish directory not found: $PublishPath"
    Write-Host "Run './build.ps1' first to build the plugin." -ForegroundColor Yellow
    exit 1
}

Write-Host "Checking publish directory: $PublishPath" -ForegroundColor Green
Write-Host ""

# Check for main plugin assembly
$pluginDll = Join-Path $PublishPath 'Jellyfin.Plugin.Template.dll'
if (-not (Test-Path $pluginDll)) {
    Write-Error "Main plugin assembly not found: Jellyfin.Plugin.Template.dll"
    exit 1
}
Write-Host "✓ Main plugin assembly found" -ForegroundColor Green

# Check for required dependencies
$requiredDlls = @(
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'SQLitePCLRaw.batteries_v2.dll'
)

Write-Host ""
Write-Host "Checking required dependencies:" -ForegroundColor Cyan
foreach ($dll in $requiredDlls) {
    $dllPath = Join-Path $PublishPath $dll
    if (Test-Path $dllPath) {
        Write-Host "  ✓ $dll" -ForegroundColor Green
    } else {
        Write-Warning "  ✗ $dll (missing)"
    }
}

# Check that no platform-specific runtime folders exist
Write-Host ""
Write-Host "Checking for platform-specific runtime folders:" -ForegroundColor Cyan
$runtimesPath = Join-Path $PublishPath 'runtimes'
if (Test-Path $runtimesPath) {
    Write-Warning "  ✗ 'runtimes' folder found (should be removed for cross-platform deployment)"
    Write-Host "    Run './build.ps1 -Install' to auto-remove, or delete manually" -ForegroundColor Yellow
} else {
    Write-Host "  ✓ No 'runtimes' folder (good for cross-platform)" -ForegroundColor Green
}

# Check for unwanted files
Write-Host ""
Write-Host "Checking for unwanted metadata files:" -ForegroundColor Cyan
$metadataFiles = Get-ChildItem -Path $PublishPath -File | Where-Object {
    $_.Name -like '*.deps.json' -or
    $_.Name -like '*.runtimeconfig.json' -or
    $_.Name -like '*.pdb' -or
    ($_.Name -like '*.xml' -and $_.Name -ne 'Jellyfin.Plugin.Template.xml')
}

if ($metadataFiles) {
    Write-Warning "  Found metadata files (should be removed for cleaner deployment):"
    foreach ($file in $metadataFiles) {
        Write-Host "    - $($file.Name)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ✓ No unwanted metadata files" -ForegroundColor Green
}

# Analyze main assembly for platform info
Write-Host ""
Write-Host "Analyzing main assembly:" -ForegroundColor Cyan

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    try {
        $asmInfo = dotnet --version 2>&1
        Write-Host "  .NET SDK version: $asmInfo" -ForegroundColor Gray

        # Use reflection to check assembly info
        $assembly = [System.Reflection.Assembly]::LoadFrom($pluginDll)
        $targetFramework = $assembly.CustomAttributes |
            Where-Object { $_.AttributeType.Name -eq 'TargetFrameworkAttribute' } |
            Select-Object -First 1

        if ($targetFramework) {
            $framework = $targetFramework.ConstructorArguments[0].Value
            Write-Host "  ✓ Target Framework: $framework" -ForegroundColor Green
        }

        $processorArch = $assembly.GetName().ProcessorArchitecture
        Write-Host "  ✓ Processor Architecture: $processorArch" -ForegroundColor Green

        if ($processorArch -eq 'MSIL' -or $processorArch -eq 'None') {
            Write-Host "  ✓ Platform-agnostic (MSIL/AnyCPU) - GOOD for cross-platform!" -ForegroundColor Green
        } else {
            Write-Warning "  ✗ Platform-specific ($processorArch) - NOT ideal for cross-platform"
        }

    } catch {
        Write-Warning "  Could not analyze assembly: $_"
    }
} else {
    Write-Warning "  dotnet CLI not found - skipping assembly analysis"
}

# List all files in publish directory
Write-Host ""
Write-Host "Files in publish directory:" -ForegroundColor Cyan
$files = Get-ChildItem -Path $PublishPath -File | Sort-Object Name
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "  Total files: $($files.Count) ($([math]::Round($totalSize/1KB, 2)) KB)" -ForegroundColor Gray
Write-Host ""

$files | Format-Table @{
    Label = "Name"
    Expression = { $_.Name }
    Width = 45
}, @{
    Label = "Size (KB)"
    Expression = { [math]::Round($_.Length/1KB, 2) }
    Width = 12
    Align = "Right"
}, @{
    Label = "Modified"
    Expression = { $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm") }
    Width = 20
} | Out-String | Write-Host

# Platform compatibility summary
Write-Host ""
Write-Host "=== Cross-Platform Compatibility Summary ===" -ForegroundColor Cyan
Write-Host ""

$issues = @()
if (Test-Path $runtimesPath) { $issues += "Runtime folders present" }
if ($metadataFiles) { $issues += "Metadata files present" }

if ($issues.Count -eq 0) {
    Write-Host "✓ Build appears to be cross-platform compatible!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Deployment recommendations:" -ForegroundColor Cyan
    Write-Host "  • Windows: Use './build.ps1 -Install -Restart'" -ForegroundColor Gray
    Write-Host "  • Linux:   Use 'pwsh ./build.ps1 -Install -Restart -Platform Linux'" -ForegroundColor Gray
    Write-Host "  • Manual:  Copy all files to Jellyfin plugins directory" -ForegroundColor Gray
} else {
    Write-Warning "⚠ Potential compatibility issues found:"
    foreach ($issue in $issues) {
        Write-Host "  • $issue" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Run './build.ps1 -Install' to auto-fix, or clean up manually." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "For detailed deployment instructions, see DEPLOYMENT.md" -ForegroundColor Cyan

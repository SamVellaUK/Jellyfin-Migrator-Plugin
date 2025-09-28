<#
    Jellyfin Migrator Plugin - Build Script

    Usage examples:
      ./build.ps1                    # Restore, build, publish (Release -> ./publish)
      ./build.ps1 -Configuration Debug -Output out\debug
      ./build.ps1 -Clean             # Clean before building
      ./build.ps1 -NoRestore         # Skip restore (faster for repeat builds)
      ./build.ps1 -Install           # Also copy to Jellyfin plugins folder
      ./build.ps1 -Install -InstallPath 'C:\Path\to\plugins\Migrator'
      ./build.ps1 -Install -Restart  # Copy and restart Jellyfin service
#>

param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [string]$Output = 'publish',

    [switch]$Clean,

    [switch]$NoRestore,

    [switch]$Install,

    [string]$InstallPath = 'C:\ProgramData\Jellyfin\Server\plugins\Migrator',

    [switch]$Restart,

    [string]$ServiceName = 'JellyfinServer',

    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Run([string]$File, [string[]]$Arguments) {
    Write-Host ">> $File $($Arguments -join ' ')"
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $File $($Arguments -join ' ') (exit $LASTEXITCODE)"
    }
}

function Restart-JellyfinService([string]$Name, [int]$TimeoutSeconds = 60) {
    try {
        $svc = Get-Service -Name $Name -ErrorAction Stop
    } catch {
        Write-Warning "Service '$Name' not found. Skipping restart. ($_ )"
        return
    }

    try {
        if ($svc.Status -eq 'Running') {
            Write-Host "Restarting service '$Name'..."
            Restart-Service -Name $Name -Force -ErrorAction Stop
        } else {
            Write-Host "Starting service '$Name' (was $($svc.Status))..."
            Start-Service -Name $Name -ErrorAction Stop
        }

        $start = Get-Date
        while ((Get-Service -Name $Name).Status -ne 'Running') {
            if ((Get-Date) - $start -gt [TimeSpan]::FromSeconds($TimeoutSeconds)) {
                throw "Service '$Name' did not reach Running within ${TimeoutSeconds}s"
            }
            Start-Sleep -Milliseconds 500
        }
        Write-Host "Service '$Name' is Running."
    } catch {
        Write-Error "Failed to restart service '$Name'. You may need to run PowerShell as Administrator. $_"
        throw
    }
}

function Stop-JellyfinService([string]$Name, [int]$TimeoutSeconds = 60) {
    try { $svc = Get-Service -Name $Name -ErrorAction Stop } catch { Write-Warning "Service '$Name' not found. Skipping stop."; return }
    if ($svc.Status -ne 'Stopped') {
        Write-Host "Stopping service '$Name'..."
        try { Stop-Service -Name $Name -Force -ErrorAction Stop } catch { Write-Error "Failed to stop service '$Name'. $_"; throw }
        $start = Get-Date
        while ((Get-Service -Name $Name).Status -ne 'Stopped') {
            if ((Get-Date) - $start -gt [TimeSpan]::FromSeconds($TimeoutSeconds)) { throw "Service '$Name' did not stop within ${TimeoutSeconds}s" }
            Start-Sleep -Milliseconds 500
        }
        Write-Host "Service '$Name' is Stopped."
    } else {
        Write-Host "Service '$Name' already Stopped."
    }
}

function Start-JellyfinService([string]$Name, [int]$TimeoutSeconds = 60) {
    try { $svc = Get-Service -Name $Name -ErrorAction Stop } catch { Write-Warning "Service '$Name' not found. Skipping start."; return }
    if ($svc.Status -ne 'Running') {
        Write-Host "Starting service '$Name'..."
        try { Start-Service -Name $Name -ErrorAction Stop } catch { Write-Error "Failed to start service '$Name'. $_"; throw }
        $start = Get-Date
        while ((Get-Service -Name $Name).Status -ne 'Running') {
            if ((Get-Date) - $start -gt [TimeSpan]::FromSeconds($TimeoutSeconds)) { throw "Service '$Name' did not start within ${TimeoutSeconds}s" }
            Start-Sleep -Milliseconds 500
        }
        Write-Host "Service '$Name' is Running."
    } else {
        Write-Host "Service '$Name' already Running."
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    $sln  = Join-Path $root 'Jellyfin.Plugin.Template.sln'
    $proj = Join-Path $root 'Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj'
    $PluginAssembly = 'Jellyfin.Plugin.Template.dll'
    $whitelist = @(
        'Jellyfin.Plugin.Template.dll',
        'Microsoft.Data.Sqlite.dll',
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll'
    )

    if ($Clean) { Run 'dotnet' @('clean', $sln, '-c', $Configuration) }
    if (-not $NoRestore) { Run 'dotnet' @('restore', $sln) }

    if ($Rebuild) { Run 'dotnet' @('build', $sln, '-c', $Configuration, '-t:Rebuild') } else { Run 'dotnet' @('build', $sln, '-c', $Configuration) }
    if (-not (Test-Path $Output)) { New-Item -ItemType Directory -Path $Output | Out-Null }
    Run 'dotnet' @('publish', $proj, '-c', $Configuration, '-o', $Output)

    $full = (Resolve-Path $Output).Path
    Write-Host "Build complete. Publish output: $full"

    if ($Install) {
        try {
            Write-Host "Installing to: $InstallPath"
            if (-not (Test-Path $InstallPath)) { New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null }

            # Stop service before replacing plugin files (prevents file locks)
            if ($Restart) { Stop-JellyfinService -Name $ServiceName }

            # Clean out existing plugin files completely
            Write-Host "Removing existing files in: $InstallPath"
            Get-ChildItem -Path $InstallPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
                try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove '$($_.FullName)': $_" }
            }

            # Copy all published files first
            Write-Host "Copying published files to install path..."
            Copy-Item -Path (Join-Path $full '*') -Destination $InstallPath -Recurse -Force

            # Remove runtime subfolders deployed by publish that are not needed and can conflict on server
            foreach ($d in @('runtimes')) {
                $dp = Join-Path $InstallPath $d
                if (Test-Path $dp) {
                    try { Remove-Item -LiteralPath $dp -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove dir '$dp': $_" }
                }
            }

            # Strict prune: keep ONLY whitelisted DLLs; remove everything else (and known metadata files)
            Write-Host "Pruning non-whitelisted assemblies from install path..."
            Get-ChildItem -Path $InstallPath -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.Extension -ieq '.dll') {
                    if ($whitelist -notcontains $_.Name) {
                        try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { Write-Warning "Could not remove '$($_.Name)': $_" }
                    }
                } elseif ($_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' -or $_.Name -like '*.pdb' -or $_.Name -like '*.xml') {
                    try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { Write-Warning "Could not remove '$($_.Name)': $_" }
                }
            }

            Write-Host "Installed plugin files at '$InstallPath':"
            Write-Host "Verifying installed files:"
            $table = Get-ChildItem -Path $InstallPath -File | Select-Object Name, Length, LastWriteTime | Format-Table | Out-String
            Write-Host $table
            foreach ($n in $whitelist) {
                $src = Join-Path $full $n; $dst = Join-Path $InstallPath $n;
                if ( (Test-Path $src) -and (Test-Path $dst) ) {
                    $s = Get-Item $src; $d = Get-Item $dst;
                    $line = "{0,-35} src:{1,10}B {2:g} -> dst:{3,10}B {4:g}" -f $n, $s.Length, $s.LastWriteTime, $d.Length, $d.LastWriteTime
                    Write-Host $line
                }
            }
        }
        catch {
            Write-Error "Failed during install to '$InstallPath'. You may need to run PowerShell as Administrator. Error: $($_.Exception.Message)"
            exit 1
        }
        # Start service after replacement (outside copy try/catch for clearer errors)
        if ($Restart) {
            try { Start-JellyfinService -Name $ServiceName } catch { Write-Error $_; exit 1 }
        }
    }
}
finally {
    Pop-Location
}

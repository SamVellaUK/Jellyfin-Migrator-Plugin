<#
    Jellyfin Migrator Plugin - Cross-Platform Build Script

    Supports both Windows and Linux deployments with automatic platform detection.

    Usage examples:
      # Basic build (auto-detects platform)
      ./build.ps1                    # Restore, build, publish (Release -> ./publish)

      # Development builds
      ./build.ps1 -Configuration Debug -Output out\debug
      ./build.ps1 -Clean             # Clean before building
      ./build.ps1 -NoRestore         # Skip restore (faster for repeat builds)

      # Windows installation
      ./build.ps1 -Install           # Install to C:\ProgramData\Jellyfin\Server\plugins\Migrator
      ./build.ps1 -Install -InstallPath 'C:\Custom\Path\plugins\Migrator'
      ./build.ps1 -Install -Restart  # Install and restart JellyfinServer service

      # Linux installation (requires PowerShell Core and sudo)
      ./build.ps1 -Install           # Install to /var/lib/jellyfin/plugins/Migrator
      ./build.ps1 -Install -InstallPath '/usr/share/jellyfin/plugins/Migrator'
      ./build.ps1 -Install -Restart  # Install and restart jellyfin systemd service

      # Force specific platform
      ./build.ps1 -Platform Linux    # Force Linux paths/service management
      ./build.ps1 -Platform Windows  # Force Windows paths/service management

      # Full rebuild with installation
      ./build.ps1 -Rebuild -Install -Restart
#>

param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',

    [string]$Output = 'publish',

    [switch]$Clean,

    [switch]$NoRestore,

    [switch]$Install,

    [string]$InstallPath = '',

    [switch]$Restart,

    [string]$ServiceName = '',

    [switch]$Rebuild,

    [ValidateSet('Auto','Windows','Linux')]
    [string]$Platform = 'Auto'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Detect OS platform
function Get-TargetPlatform {
    if ($Platform -ne 'Auto') { return $Platform }

    # PowerShell Core (6.0+) has $IsLinux, $IsWindows, $IsMacOS
    # Windows PowerShell 5.1 only has $env:OS
    if (Test-Path variable:IsLinux) {
        if ($IsLinux) { return 'Linux' }
        if ($IsMacOS) { return 'Linux' }  # macOS uses same approach as Linux
        if ($IsWindows) { return 'Windows' }
    }

    # Fallback for Windows PowerShell 5.1
    if ($env:OS -eq 'Windows_NT') { return 'Windows' }

    throw "Unable to detect platform. Please specify -Platform explicitly."
}

$DetectedPlatform = Get-TargetPlatform
Write-Host "Target platform: $DetectedPlatform"

# Set default paths based on platform
if ([string]::IsNullOrEmpty($InstallPath)) {
    if ($DetectedPlatform -eq 'Linux') {
        $InstallPath = '/var/lib/jellyfin/plugins/Migrator'
    } else {
        $InstallPath = 'C:\ProgramData\Jellyfin\Server\plugins\Migrator'
    }
}

if ([string]::IsNullOrEmpty($ServiceName)) {
    if ($DetectedPlatform -eq 'Linux') {
        $ServiceName = 'jellyfin'
    } else {
        $ServiceName = 'JellyfinServer'
    }
}

function Run([string]$File, [string[]]$Arguments) {
    Write-Host ">> $File $($Arguments -join ' ')"
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $File $($Arguments -join ' ') (exit $LASTEXITCODE)"
    }
}

function Restart-JellyfinService([string]$Name, [int]$TimeoutSeconds = 60) {
    if ($DetectedPlatform -eq 'Linux') {
        Restart-SystemdService -Name $Name -TimeoutSeconds $TimeoutSeconds
        return
    }

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
    if ($DetectedPlatform -eq 'Linux') {
        Stop-SystemdService -Name $Name -TimeoutSeconds $TimeoutSeconds
        return
    }

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
    if ($DetectedPlatform -eq 'Linux') {
        Start-SystemdService -Name $Name -TimeoutSeconds $TimeoutSeconds
        return
    }

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

# Linux systemd service management functions
function Restart-SystemdService([string]$Name, [int]$TimeoutSeconds = 60) {
    Write-Host "Restarting systemd service '$Name'..."
    try {
        $output = sudo systemctl restart $Name 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "systemctl restart failed: $output"
        }
        Start-Sleep -Seconds 2
        $status = sudo systemctl is-active $Name 2>&1
        if ($status -ne 'active') {
            throw "Service '$Name' is not active after restart (status: $status)"
        }
        Write-Host "Service '$Name' restarted successfully."
    } catch {
        Write-Error "Failed to restart systemd service '$Name'. You may need to run with sudo. $_"
        throw
    }
}

function Stop-SystemdService([string]$Name, [int]$TimeoutSeconds = 60) {
    Write-Host "Stopping systemd service '$Name'..."
    try {
        $output = sudo systemctl stop $Name 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "systemctl stop had non-zero exit: $output"
        }
        Start-Sleep -Seconds 1
        Write-Host "Service '$Name' stopped."
    } catch {
        Write-Error "Failed to stop systemd service '$Name'. $_"
        throw
    }
}

function Start-SystemdService([string]$Name, [int]$TimeoutSeconds = 60) {
    Write-Host "Starting systemd service '$Name'..."
    try {
        $output = sudo systemctl start $Name 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "systemctl start failed: $output"
        }
        Start-Sleep -Seconds 2
        $status = sudo systemctl is-active $Name 2>&1
        if ($status -ne 'active') {
            throw "Service '$Name' is not active after start (status: $status)"
        }
        Write-Host "Service '$Name' started successfully."
    } catch {
        Write-Error "Failed to start systemd service '$Name'. $_"
        throw
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

    # Build with AnyCPU for cross-platform compatibility
    if ($Rebuild) {
        Run 'dotnet' @('build', $sln, '-c', $Configuration, '-t:Rebuild')
    } else {
        Run 'dotnet' @('build', $sln, '-c', $Configuration)
    }

    if (-not (Test-Path $Output)) { New-Item -ItemType Directory -Path $Output | Out-Null }

    # Publish without runtime identifier (framework-dependent, cross-platform)
    Run 'dotnet' @('publish', $proj, '-c', $Configuration, '-o', $Output, '--no-build')

    $full = (Resolve-Path $Output).Path
    Write-Host "Build complete. Publish output: $full"

    if ($Install) {
        try {
            Write-Host "Installing to: $InstallPath"

            # Create install directory (with sudo on Linux if needed)
            if (-not (Test-Path $InstallPath)) {
                if ($DetectedPlatform -eq 'Linux') {
                    Write-Host "Creating plugin directory (may require sudo)..."
                    $mkdirResult = sudo mkdir -p $InstallPath 2>&1
                    if ($LASTEXITCODE -ne 0) {
                        throw "Failed to create directory: $mkdirResult"
                    }
                    # Set permissions for jellyfin user
                    sudo chown -R jellyfin:jellyfin $InstallPath 2>&1 | Out-Null
                } else {
                    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
                }
            }

            # Stop service before replacing plugin files (prevents file locks)
            if ($Restart) { Stop-JellyfinService -Name $ServiceName }

            # Clean out existing plugin files completely
            Write-Host "Removing existing files in: $InstallPath"
            if ($DetectedPlatform -eq 'Linux') {
                sudo rm -rf "$InstallPath/*" 2>&1 | Out-Null
            } else {
                Get-ChildItem -Path $InstallPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
                    try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove '$($_.FullName)': $_" }
                }
            }

            # Copy all published files first
            Write-Host "Copying published files to install path..."
            if ($DetectedPlatform -eq 'Linux') {
                sudo cp -r "$full/*" $InstallPath/ 2>&1
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to copy files to $InstallPath"
                }
                sudo chown -R jellyfin:jellyfin $InstallPath 2>&1 | Out-Null
            } else {
                Copy-Item -Path (Join-Path $full '*') -Destination $InstallPath -Recurse -Force
            }

            # Remove runtime subfolders deployed by publish that are not needed and can conflict on server
            foreach ($d in @('runtimes')) {
                $dp = Join-Path $InstallPath $d
                if (Test-Path $dp) {
                    if ($DetectedPlatform -eq 'Linux') {
                        sudo rm -rf $dp 2>&1 | Out-Null
                    } else {
                        try { Remove-Item -LiteralPath $dp -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove dir '$dp': $_" }
                    }
                }
            }

            # Strict prune: keep ONLY whitelisted DLLs; remove everything else (and known metadata files)
            Write-Host "Pruning non-whitelisted assemblies from install path..."
            Get-ChildItem -Path $InstallPath -File -ErrorAction SilentlyContinue | ForEach-Object {
                $shouldRemove = $false
                if ($_.Extension -ieq '.dll') {
                    if ($whitelist -notcontains $_.Name) {
                        $shouldRemove = $true
                    }
                } elseif ($_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' -or $_.Name -like '*.pdb' -or $_.Name -like '*.xml') {
                    $shouldRemove = $true
                }

                if ($shouldRemove) {
                    if ($DetectedPlatform -eq 'Linux') {
                        sudo rm -f "$($_.FullName)" 2>&1 | Out-Null
                    } else {
                        try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch { Write-Warning "Could not remove '$($_.Name)': $_" }
                    }
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

# Cross-Platform Build System - Implementation Summary

## Overview

The Jellyfin Migrator Plugin has been enhanced with full cross-platform support for Windows and Linux deployments. The plugin now builds as a platform-agnostic .NET assembly that can run on any platform with .NET 8.0 runtime installed.

## Verification Status

✅ **Project is cross-platform compatible:**
- **.NET 8.0 framework** - Cross-platform runtime
- **Microsoft.Data.Sqlite** - Works on Windows, Linux, macOS
- **Jellyfin APIs** - Platform-agnostic
- **No native dependencies** - No P/Invoke or COM interop
- **Standard .NET APIs** - System.IO, System.Text.Json, etc.

## Changes Made

### 1. Project Configuration (`Jellyfin.Plugin.Template.csproj`)

**Added:**
```xml
<PlatformTarget>AnyCPU</PlatformTarget>
<RuntimeIdentifier></RuntimeIdentifier>
```

**Purpose:** Ensures the build produces platform-agnostic MSIL code without runtime-specific binaries.

### 2. Build Script (`build.ps1`)

#### New Parameters:
- `-Platform` - Force specific platform (`Auto`, `Windows`, `Linux`)
- `-InstallPath` - Now empty by default, auto-detected per platform
- `-ServiceName` - Now empty by default, auto-detected per platform

#### Platform Detection:
```powershell
function Get-TargetPlatform {
    if ($Platform -ne 'Auto') { return $Platform }
    if ($IsLinux) { return 'Linux' }
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { return 'Windows' }
    throw "Unable to detect platform..."
}
```

#### Default Paths by Platform:

| Platform | Plugin Path | Service Name |
|----------|------------|--------------|
| Windows  | `C:\ProgramData\Jellyfin\Server\plugins\Migrator` | `JellyfinServer` |
| Linux    | `/var/lib/jellyfin/plugins/Migrator` | `jellyfin` |

#### Service Management:

**Windows:**
- Uses `Get-Service`, `Start-Service`, `Stop-Service`, `Restart-Service`
- Manages Windows Services API

**Linux:**
- Uses `systemctl` via sudo
- Functions: `Restart-SystemdService`, `Stop-SystemdService`, `Start-SystemdService`
- Proper timeout handling and status checking

#### File Operations:

**Windows:**
- PowerShell cmdlets: `Copy-Item`, `Remove-Item`, `New-Item`

**Linux:**
- Shell commands via sudo: `cp -r`, `rm -rf`, `mkdir -p`
- Automatic ownership: `chown -R jellyfin:jellyfin`
- Proper permission handling

#### Build Process:
```powershell
# Platform-agnostic build
dotnet build $sln -c $Configuration

# Framework-dependent publish (no runtime identifier)
dotnet publish $proj -c $Configuration -o $Output --no-build
```

### 3. Documentation

#### New Files:

**`DEPLOYMENT.md`** - Comprehensive cross-platform deployment guide:
- Platform compatibility matrix
- Installation instructions (Windows/Linux)
- Service management details
- Troubleshooting section
- Platform differences table
- CI/CD integration examples

**`verify-build.ps1`** - Build verification script:
- Checks for platform-agnostic assemblies
- Validates required dependencies
- Detects platform-specific artifacts
- Reports compatibility issues
- Provides deployment recommendations

#### Updated Files:

**`CLAUDE.md`** - Added cross-platform section:
- Platform compatibility status
- Build configuration details
- Default installation paths
- Service management approach
- Build script features

**`build.ps1` header** - Updated usage examples:
- Windows-specific examples
- Linux-specific examples
- Platform override examples
- PowerShell Core requirements

## Usage Examples

### Windows Deployment

```powershell
# Standard deployment
./build.ps1 -Rebuild -Install -Restart

# Custom path
./build.ps1 -Install -InstallPath "C:\Custom\Jellyfin\plugins\Migrator"

# Build only (no install)
./build.ps1
```

### Linux Deployment

```bash
# Standard deployment (requires PowerShell Core)
pwsh ./build.ps1 -Rebuild -Install -Restart

# Custom path
pwsh ./build.ps1 -Install -InstallPath "/usr/share/jellyfin/plugins/Migrator"

# Force platform detection
pwsh ./build.ps1 -Platform Linux -Install -Restart

# Build only
dotnet build -c Release
```

### Build Verification

```bash
# After building, verify cross-platform compatibility
pwsh ./verify-build.ps1
```

## Technical Details

### Assembly Characteristics

**Target Framework:** net8.0
**Platform Target:** AnyCPU
**Architecture:** MSIL (platform-agnostic IL code)
**Deployment Type:** Framework-dependent (requires .NET 8.0 runtime)
**Runtime Identifier:** None (cross-platform)

### Dependencies

All dependencies are cross-platform:

- **Jellyfin.Controller** 10.9.11 - Platform-agnostic
- **Jellyfin.Model** 10.9.11 - Platform-agnostic
- **Microsoft.Data.Sqlite** 8.0.8 - Cross-platform SQLite support
- **SQLitePCLRaw** packages - Cross-platform native SQLite bindings

### File Structure (After Build)

Whitelisted files (deployed):
- `Jellyfin.Plugin.Template.dll` - Main plugin assembly
- `Microsoft.Data.Sqlite.dll` - SQLite managed wrapper
- `SQLitePCLRaw.core.dll` - SQLite core
- `SQLitePCLRaw.provider.e_sqlite3.dll` - SQLite provider
- `SQLitePCLRaw.batteries_v2.dll` - SQLite batteries

Removed files (build script cleans):
- `runtimes/` folder - Platform-specific natives (conflicts)
- `*.deps.json` - Dependency metadata
- `*.runtimeconfig.json` - Runtime configuration
- `*.pdb` - Debug symbols
- Non-whitelisted DLLs - Unnecessary assemblies

## Platform-Specific Behavior

### Windows

**Service Management:**
- Direct Windows Service API integration
- Synchronous service state monitoring
- Administrator privileges required

**File Operations:**
- PowerShell native cmdlets
- NTFS permissions and ACLs
- Windows path separators (`\`)

### Linux

**Service Management:**
- systemd via `systemctl` commands
- Sudo elevation for privileged operations
- Async status checking with sleep delays

**File Operations:**
- Shell commands with sudo elevation
- POSIX permissions (chown/chmod)
- Unix path separators (`/`)
- Automatic jellyfin:jellyfin ownership

## Testing Checklist

### Pre-Deployment
- [ ] Run `dotnet build` successfully
- [ ] Run `./verify-build.ps1` - all checks pass
- [ ] Confirm no `runtimes/` folder in output
- [ ] Verify processor architecture is MSIL/AnyCPU

### Windows Testing
- [ ] Build with `./build.ps1`
- [ ] Install with `-Install -Restart`
- [ ] Verify service restarts correctly
- [ ] Check plugin appears in Jellyfin dashboard
- [ ] Run export task

### Linux Testing
- [ ] Build with `pwsh ./build.ps1`
- [ ] Install with `-Install -Restart -Platform Linux`
- [ ] Verify systemd service restarts
- [ ] Check file ownership (jellyfin:jellyfin)
- [ ] Check plugin appears in Jellyfin dashboard
- [ ] Run export task

## Migration Notes

### For Existing Installations

**No code changes required** - The plugin code is already cross-platform compatible.

**Build changes:**
- Old: Built with default settings (may include runtime folders)
- New: Explicitly targets AnyCPU, removes runtime folders

**Deployment changes:**
- Old: Manual copy required for Linux
- New: Automated with platform detection

### For Developers

**Breaking changes:** None

**New capabilities:**
- Automatic platform detection
- Platform-specific service management
- Cross-platform file operations
- Unified build script for all platforms

## Future Enhancements

Potential improvements:
- [ ] Add macOS testing and service management (launchd)
- [ ] Docker container deployment support
- [ ] GitHub Actions CI/CD workflow
- [ ] Automated cross-platform testing
- [ ] Self-contained deployment option (includes .NET runtime)
- [ ] FreeBSD support

## References

- [.NET Cross-Platform Development](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [Jellyfin Plugin Development](https://jellyfin.org/docs/general/server/plugins/)
- [PowerShell Core on Linux](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-linux)
- [systemd Service Management](https://www.freedesktop.org/software/systemd/man/systemctl.html)

## Support

For issues related to cross-platform deployment:
1. Check `DEPLOYMENT.md` for troubleshooting steps
2. Run `./verify-build.ps1` to diagnose build issues
3. Review Jellyfin logs for runtime errors
4. Ensure .NET 8.0 runtime is installed on target platform

---

**Summary:** The Jellyfin Migrator Plugin now fully supports cross-platform deployment with automatic platform detection, unified build process, and comprehensive documentation. No code changes were needed - only build configuration and deployment automation.

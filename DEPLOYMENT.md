# Cross-Platform Deployment Guide

This plugin is built as a **cross-platform .NET assembly** that works on both Windows and Linux Jellyfin installations.

## Platform Compatibility

✅ **Windows** - Native support
✅ **Linux** - Full support (tested on Ubuntu, Debian, Fedora, Arch)
✅ **macOS** - Should work (untested)

The plugin uses:
- **.NET 8.0** runtime (cross-platform)
- **Microsoft.Data.Sqlite** (works on all platforms)
- **Jellyfin Controller API** (platform-agnostic)

## Building the Plugin

### Prerequisites

**Windows:**
- .NET 8.0 SDK
- PowerShell 5.1+ (built-in)

**Linux:**
- .NET 8.0 SDK
- PowerShell Core 7.0+
- sudo access (for installation)

### Basic Build

```bash
# Auto-detects platform and builds
./build.ps1
```

### Build for Specific Platform

```bash
# Force Linux configuration
./build.ps1 -Platform Linux

# Force Windows configuration
./build.ps1 -Platform Windows
```

## Installation

### Windows Installation

```powershell
# Install to default location (requires Administrator)
./build.ps1 -Rebuild -Install -Restart

# Custom installation path
./build.ps1 -Install -InstallPath "C:\Custom\Jellyfin\plugins\Migrator"

# Install without restarting service
./build.ps1 -Install
```

**Default Windows Paths:**
- Plugin: `C:\ProgramData\Jellyfin\Server\plugins\Migrator`
- Service: `JellyfinServer`

### Linux Installation

```bash
# Install to default location (requires sudo)
pwsh ./build.ps1 -Rebuild -Install -Restart

# Custom installation path
pwsh ./build.ps1 -Install -InstallPath "/usr/share/jellyfin/plugins/Migrator"

# Install without restarting service
pwsh ./build.ps1 -Install
```

**Default Linux Paths:**
- Plugin: `/var/lib/jellyfin/plugins/Migrator`
- Service: `jellyfin` (systemd)

### Manual Installation

If you prefer not to use the build script:

1. Build the plugin:
   ```bash
   dotnet publish Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj -c Release -o publish
   ```

2. Copy files to Jellyfin plugins directory:

   **Windows:**
   ```powershell
   Copy-Item publish\* "C:\ProgramData\Jellyfin\Server\plugins\Migrator\" -Recurse -Force
   ```

   **Linux:**
   ```bash
   sudo mkdir -p /var/lib/jellyfin/plugins/Migrator
   sudo cp -r publish/* /var/lib/jellyfin/plugins/Migrator/
   sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Migrator
   ```

3. Remove unnecessary files (optional but recommended):
   ```bash
   # Remove runtime folders that can cause conflicts
   rm -rf /var/lib/jellyfin/plugins/Migrator/runtimes

   # Remove non-whitelisted DLLs and metadata files
   # Keep only: Jellyfin.Plugin.Template.dll, Microsoft.Data.Sqlite.dll,
   #            SQLitePCLRaw.*.dll
   ```

4. Restart Jellyfin:

   **Windows:**
   ```powershell
   Restart-Service JellyfinServer
   ```

   **Linux:**
   ```bash
   sudo systemctl restart jellyfin
   ```

## Verifying Installation

1. Open Jellyfin web interface
2. Navigate to **Dashboard → Plugins**
3. Look for **"Jellyfin Migrator Plugin"** in the installed plugins list
4. Check version and ensure it's active

## Troubleshooting

### Linux: Permission Denied

```bash
# Ensure correct ownership
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Migrator

# Ensure correct permissions
sudo chmod -R 755 /var/lib/jellyfin/plugins/Migrator
```

### Linux: Service Not Found

```bash
# Check if Jellyfin service exists
systemctl status jellyfin

# If service has different name, specify it
pwsh ./build.ps1 -Install -Restart -ServiceName "jellyfin.service"
```

### Plugin Not Loading

1. Check Jellyfin logs:

   **Windows:** `C:\ProgramData\Jellyfin\Server\log\`
   **Linux:** `/var/log/jellyfin/` or `journalctl -u jellyfin`

2. Ensure .NET 8.0 runtime is installed:
   ```bash
   dotnet --list-runtimes
   ```

3. Verify all required DLLs are present:
   ```bash
   ls -la /var/lib/jellyfin/plugins/Migrator/
   ```

### Build Errors on Linux

```bash
# Install PowerShell Core if missing
# Ubuntu/Debian:
sudo apt-get install -y powershell

# Fedora:
sudo dnf install -y powershell

# Arch:
yay -S powershell-bin
```

## Platform Differences

| Feature | Windows | Linux |
|---------|---------|-------|
| Service Management | Windows Service API | systemd |
| Default Plugin Path | `C:\ProgramData\Jellyfin\Server\plugins` | `/var/lib/jellyfin/plugins` |
| Default Service Name | `JellyfinServer` | `jellyfin` |
| File Operations | PowerShell cmdlets | Shell commands via sudo |
| Permissions | ACLs | POSIX permissions |

## Advanced Usage

### Development Build on Linux

```bash
# Build debug version without installation
pwsh ./build.ps1 -Configuration Debug -NoRestore

# Build and install debug version
pwsh ./build.ps1 -Configuration Debug -Install -Platform Linux
```

### Custom Jellyfin Installation

```bash
# For Docker or custom installations
pwsh ./build.ps1 -Install \
  -InstallPath "/custom/jellyfin/plugins/Migrator" \
  -ServiceName "jellyfin-custom" \
  -Platform Linux
```

### CI/CD Integration

```yaml
# Example GitHub Actions workflow
- name: Build plugin
  run: pwsh ./build.ps1 -Configuration Release

- name: Install on Linux
  run: pwsh ./build.ps1 -Install -Platform Linux
  if: runner.os == 'Linux'

- name: Install on Windows
  run: ./build.ps1 -Install -Platform Windows
  if: runner.os == 'Windows'
```

## Technical Notes

- Plugin is built as **AnyCPU** (platform-agnostic IL)
- No native dependencies or P/Invoke calls
- SQLite native libraries handled by Microsoft.Data.Sqlite NuGet package
- Framework-dependent deployment (requires .NET 8.0 runtime on target)
- Compatible with Jellyfin 10.9.x+

## Support

For platform-specific issues:
- Check Jellyfin community forums
- Review GitHub issues
- Verify .NET runtime compatibility

# Quick Start - Cross-Platform Deployment

## Version Compatibility

✅ **Works with Jellyfin 10.9.x - 10.10.x** (tested on 10.9.11 and 10.10.7)

The plugin uses version ranges (`10.9.*`) for broad compatibility - one build works across versions!

See [VERSION_COMPATIBILITY.md](VERSION_COMPATIBILITY.md) for details.

## Prerequisites

### Windows
- ✅ .NET 8.0 SDK installed
- ✅ PowerShell 5.1+ (built-in)
- ✅ Administrator privileges (for installation)

### Linux
- ✅ .NET 8.0 SDK installed
- ✅ PowerShell Core 7.0+ installed
- ✅ sudo access (for installation)

## One-Line Install

### Windows (PowerShell as Administrator)
```powershell
./build.ps1 -Rebuild -Install -Restart
```

### Linux (with sudo)
```bash
pwsh ./build.ps1 -Rebuild -Install -Restart
```

## What This Does

1. **Restores** NuGet packages
2. **Rebuilds** the plugin from scratch
3. **Publishes** to `./publish` directory
4. **Installs** to Jellyfin plugins folder
5. **Restarts** Jellyfin service

## Default Locations

| Platform | Plugin Path | Service |
|----------|-------------|---------|
| Windows  | `C:\ProgramData\Jellyfin\Server\plugins\Migrator` | `JellyfinServer` |
| Linux    | `/var/lib/jellyfin/plugins/Migrator` | `jellyfin` |

## Verify Installation

1. Open Jellyfin web interface
2. Go to **Dashboard** → **Plugins**
3. Look for **"Jellyfin Migrator Plugin"**

## Common Commands

### Build Only (No Install)
```bash
# Windows
./build.ps1

# Linux
pwsh ./build.ps1
```

### Install to Custom Path
```bash
# Windows
./build.ps1 -Install -InstallPath "C:\Custom\Path\Migrator"

# Linux
pwsh ./build.ps1 -Install -InstallPath "/custom/path/Migrator"
```

### Build Without Restarting Service
```bash
# Windows
./build.ps1 -Install

# Linux
pwsh ./build.ps1 -Install
```

### Verify Build Output
```bash
pwsh ./verify-build.ps1
```

## Troubleshooting

### Windows: Access Denied
**Solution:** Run PowerShell as Administrator

### Linux: Permission Denied
**Solution:** Ensure sudo access and run with `pwsh`

### Plugin Not Loading
**Solution:** Check Jellyfin logs and verify .NET 8.0 runtime:
```bash
dotnet --list-runtimes
```

### Build Errors
**Solution:** Clean and rebuild:
```bash
dotnet clean
pwsh ./build.ps1 -Clean -Rebuild
```

## Next Steps

1. ✅ Install plugin (see above)
2. 📖 Configure settings in Jellyfin Dashboard
3. 🔍 Select users to export
4. ▶️ Run "Jellyfin Migrator Export" task
5. 📁 Find exports in configured directory

## Documentation

- **[DEPLOYMENT.md](DEPLOYMENT.md)** - Full deployment guide
- **[CROSS_PLATFORM_CHANGES.md](CROSS_PLATFORM_CHANGES.md)** - Technical details
- **[CLAUDE.md](CLAUDE.md)** - Development guide

## Support

**Issues?** Check the troubleshooting section in [DEPLOYMENT.md](DEPLOYMENT.md)

**Questions?** Review the full documentation or check Jellyfin logs.

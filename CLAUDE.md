# Claude Code Configuration

This file contains configuration and context for the Claude Code AI assistant when working on this project.

## Project Overview

**Project Name:** Jellyfin Migrator Plugin
**Type:** C# .NET Jellyfin Plugin
**Purpose:** Export user data, library permissions, passwords, and other migration data from Jellyfin servers
**Platform Support:** Cross-platform (Windows, Linux, macOS)

## Build & Development Commands

### Build Commands

**Windows:**
```powershell
# Build and install (run as administrator)
./build.ps1 -Rebuild -Install -Restart

# Build only (no install/restart)
dotnet build

# Clean build
dotnet clean && dotnet build
```

**Linux:**
```bash
# Build and install (requires PowerShell Core and sudo)
pwsh ./build.ps1 -Rebuild -Install -Restart

# Build only
dotnet build

# Clean build
dotnet clean && dotnet build

# Force Linux platform detection
pwsh ./build.ps1 -Platform Linux -Install -Restart
```

### Test Commands
```bash
# Run linting/style checks
dotnet build -c Release

# The project uses StyleCop analyzers and code analysis rules
```

## Project Structure

```
Jellyfin.Plugin.Template/
├── Configuration/
│   ├── PluginConfiguration.cs    # Plugin settings/config
│   └── configPage.html           # Web UI configuration page
├── Export/
│   ├── ExportService.cs          # Main export coordinator (simplified)
│   ├── ExportLogger.cs           # Logging utilities
│   ├── UserExporter.cs           # User data export logic
│   └── ExportMigrationTask.cs    # Scheduled task implementation
└── Plugin.cs                     # Main plugin entry point
```

## Key Features

- **User Export:** Exports user ID, username, password hashes, and library permissions
- **Database & File Support:** Can export from SQLite database or file-based user data
- **Library Permissions:** Maps user access to specific libraries with names and IDs
- **Web UI:** Configuration interface for selecting users and export options
- **API Support:** Can be triggered programmatically via Jellyfin's scheduled task API
- **Logging:** Comprehensive logging for debugging and monitoring
- **Cross-Platform:** Runs on Windows, Linux, and macOS with automatic platform detection

## Cross-Platform Support

### Platform Compatibility
✅ **Windows** - Full support with Windows Service management
✅ **Linux** - Full support with systemd service management
✅ **macOS** - Should work (untested)

### Build Configuration
- Target: **.NET 8.0** (cross-platform runtime)
- Platform: **AnyCPU** (platform-agnostic IL code)
- Deployment: **Framework-dependent** (requires .NET runtime on target)
- No native dependencies or platform-specific P/Invoke

### Default Installation Paths
- **Windows:** `C:\ProgramData\Jellyfin\Server\plugins\Migrator`
- **Linux:** `/var/lib/jellyfin/plugins/Migrator`

### Service Management
- **Windows:** Uses Windows Service API (`JellyfinServer` service)
- **Linux:** Uses systemd API (`jellyfin` service via `systemctl`)

### Build Script Features
- Automatic platform detection via `$IsLinux` / `$IsWindows`
- Cross-platform file operations (PowerShell cmdlets vs shell commands)
- Platform-aware service restart (Windows Service vs systemd)
- Proper Linux permissions handling (chown jellyfin:jellyfin)

See [DEPLOYMENT.md](DEPLOYMENT.md) for detailed cross-platform deployment instructions.

## Development Notes

### Code Style
- Uses StyleCop analyzers with strict rules
- Requires XML documentation for public members
- Enforces async/await with ConfigureAwait(false)
- String operations must specify StringComparison for culture handling

### Common Issues
- **Case Sensitivity:** Database GUIDs have dashes, config GUIDs don't - use normalized comparison
- **Async Operations:** Always use ConfigureAwait(false) and proper async patterns
- **SQL Injection:** Use parameterized queries, avoid dynamic SQL construction
- **File Paths:** Use absolute paths, cross-platform path handling (Path.Combine, not hardcoded separators)
- **Platform Detection:** Build script auto-detects OS; can override with -Platform parameter
- **Linux Permissions:** Plugin files must be owned by jellyfin:jellyfin user/group

### Architecture
- **ExportService:** Simple coordinator that manages the export workflow
- **UserExporter:** Handles the core user data extraction and processing
- **ExportLogger:** Centralized logging with timestamped messages
- **ExportMigrationTask:** Scheduled task wrapper for API integration

## Testing

### Manual Testing
1. Configure plugin through Jellyfin web UI
2. Select users to export
3. Click "Run Export Now" or use API
4. Check export logs and output files

### API Testing
```bash
# Get scheduled tasks
curl -H "Authorization: MediaBrowser Token=\"your-api-key\"" \
     http://localhost:8096/ScheduledTasks

# Start export task
curl -X POST \
     -H "Authorization: MediaBrowser Token=\"your-api-key\"" \
     http://localhost:8096/ScheduledTasks/Running/{taskId}
```

## Output Files

- `users_basic.json` - Main export file with user data
- `export.log` - Detailed operation log
- Files are saved to configured export directory or default Jellyfin data path

## Dependencies

- .NET 8.0
- Jellyfin.Controller (for plugin framework)
- Microsoft.Data.Sqlite (for database access)
- System.Text.Json (for JSON processing)

## Recent Changes

### Simplified Architecture (Latest)
- Refactored from single 509-line file to modular 4-file structure
- Separated logging, user export, and coordination concerns
- Reduced complexity while maintaining full functionality
- Added comprehensive debugging for library lookup issues

### Export Enhancements
- Added password hash export from database
- Added library permissions with name and ID mapping
- Fixed case sensitivity issues with GUID comparisons
- Improved error handling and logging

## Code Review Findings (Latest)

### Current Implementation Strengths
✅ Clean separation of concerns with DI pattern
✅ Robust GUID normalization for case-insensitive matching
✅ Comprehensive timestamped logging system
✅ Multiple database path search strategies
✅ Proper async/await patterns with ConfigureAwait(false)

### Known Issues & Technical Debt

**Priority 1 - Type Safety**
- **Issue:** UserExporter.cs:239, 158 uses `dynamic` types instead of proper Jellyfin types
- **Impact:** Defeats compile-time safety, incompatible with strict StyleCop rules
- **Fix Required:** Replace with `MediaBrowser.Controller.Entities.User` type

**Priority 2 - Reflection Fragility**
- **Issue:** UserExporter.cs:249-252 uses reflection for UserViewQuery property access
- **Impact:** Silently fails if Jellyfin API changes, double property lookup overhead
- **Fix Required:** Use documented Jellyfin API for UserViewQuery initialization

**Priority 3 - Silent Failures**
- **Issue:** ExportLogger.cs:77-80, 100-103 catches all exceptions without logging
- **Impact:** Makes debugging difficult, violations go unnoticed
- **Fix Required:** Log at warning level minimum

**Priority 4 - Security**
- **Issue:** UserExporter.cs:277-286 fallback grants ALL libraries on failure
- **Impact:** Potential unauthorized library access during migration
- **Fix Required:** Return empty list on failure or throw exception

**Priority 5 - Cancellation**
- **Issue:** Database operations don't respect cancellation tokens
- **Impact:** Long-running exports can't be canceled cleanly
- **Fix Required:** Add cancellation checks in database loops

## Troubleshooting

### Common Build Issues
- Run PowerShell as Administrator for build script
- Ensure .NET 8.0 SDK is installed
- Check StyleCop warnings - they become build errors in Release mode

### Common Runtime Issues
- Empty libraries array: Check database table names and structure
- User filtering not working: Verify GUID format normalization
- Permission denied: Ensure export directory is writable

## Future Enhancements

Potential areas for expansion:
- Watch history export
- Device information export
- Library metadata export
- Import functionality for target servers
- Incremental export support
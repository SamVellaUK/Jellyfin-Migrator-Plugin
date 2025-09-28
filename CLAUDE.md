# Claude Code Configuration

This file contains configuration and context for the Claude Code AI assistant when working on this project.

## Project Overview

**Project Name:** Jellyfin Migrator Plugin
**Type:** C# .NET Jellyfin Plugin
**Purpose:** Export user data, library permissions, passwords, and other migration data from Jellyfin servers

## Build & Development Commands

### Build Commands
```bash
# Build the project (run as administrator)
./build.ps1 -Rebuild -Install -Restart

# Build only (no install/restart)
dotnet build

# Clean build
dotnet clean && dotnet build
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
- **File Paths:** Use absolute paths, handle Windows path separators

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
- Refactored from single 509-line file to modular 3-file structure
- Separated logging, user export, and coordination concerns
- Reduced complexity while maintaining full functionality
- Added comprehensive debugging for library lookup issues

### Export Enhancements
- Added password hash export from database
- Added library permissions with name and ID mapping
- Fixed case sensitivity issues with GUID comparisons
- Improved error handling and logging

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
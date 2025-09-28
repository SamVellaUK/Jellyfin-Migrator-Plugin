# Jellyfin Migrator Plugin - User Export Test Loop

This document describes the complete test loop for validating user exports in the Jellyfin Migrator Plugin.

## Overview

The test loop validates that the export functionality correctly extracts user data from a Jellyfin instance and creates properly formatted migration files.

## Prerequisites

- Jellyfin server running (typically on http://localhost:8096)
- Jellyfin Migrator Plugin installed and configured
- Valid API key configured in plugin settings
- Test users created in Jellyfin instance
- Plugin configuration saved with selected users

## Test Environment Setup

### 1. Build and Install Plugin

```powershell
./build.ps1 -Rebuild -Install -Restart
```

This command:
- Rebuilds the plugin from source
- Installs it to the Jellyfin plugins directory
- Restarts the Jellyfin server

### 2. Configure Plugin

1. Navigate to Jellyfin Dashboard → Plugins → Migrator → Settings
2. Select users to export (test users: "Family", "Mitra")
3. Set export directory (default: `./Exports/`)
4. Save configuration

## Test Execution Loop

### Step 1: Trigger Export via API

Use the PowerShell script to trigger the export programmatically:

```powershell
# File: test-export.ps1
$server = "http://localhost:8096"
$apiKey = "your-api-key-here"
$headers = @{ Authorization = "MediaBrowser Token=`"$apiKey`"" }

try {
    # Get scheduled tasks
    $tasks = Invoke-RestMethod -Uri "$server/ScheduledTasks" -Headers $headers -Method Get

    # Find migration task
    $migrationTask = $tasks | Where-Object { $_.Key -eq "JellyfinMigratorExport" }

    if ($migrationTask) {
        Write-Host "Found migration task: $($migrationTask.Name)"
        Write-Host "Current State: $($migrationTask.State)"

        # Start the export
        $taskId = $migrationTask.Id
        Invoke-RestMethod -Uri "$server/ScheduledTasks/Running/$taskId" -Headers $headers -Method Post
        Write-Host "Migration export started successfully"
    } else {
        Write-Host "Migration task not found"
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
```

Execute: `powershell -ExecutionPolicy Bypass -File "test-export.ps1"`

### Step 2: Validate Export Execution

Check the export log for successful completion:

**File**: `Exports/export.log`

**Expected Log Pattern**:
```
HH:MM:SS Migrator export started (simplified mode)
HH:MM:SS Data path: C:\ProgramData\Jellyfin\Server\data
HH:MM:SS Export directory: [export-path]
HH:MM:SS Selected user IDs filter: [user-ids] (N specific users)
HH:MM:SS Starting user export (basic mode - username and ID only)
HH:MM:SS Using database: [jellyfin-db-path]
HH:MM:SS Scanning database for users...
HH:MM:SS Found user #N: ID=[USER-GUID] Username=[USERNAME]
HH:MM:SS Filter check: ID [USER-GUID] is INCLUDED/EXCLUDED
HH:MM:SS Adding user [USERNAME] to export
HH:MM:SS Database scan complete: X total users, Y filtered out, Z to export
HH:MM:SS Export completed successfully. Z users exported.
```

### Step 3: Validate Export Output

Check the generated user data file:

**File**: `Exports/users_basic.json`

**Expected Structure**:
```json
[
  {
    "id": "GUID-FORMAT",
    "username": "string",
    "passwordHash": "$PBKDF2-SHA512$iterations=210000$SALT$HASH",
    "libraries": [
      {
        "id": "LIBRARY-GUID",
        "name": "Library Name",
        "type": "movies|tvshows|music|books|photos|etc"
      }
    ]
  }
]
```

## Validation Criteria

### ✅ Success Indicators

1. **API Response**: Export task starts without errors
2. **Log Completion**: Log ends with "Export completed successfully"
3. **File Creation**: Both `export.log` and `users_basic.json` created
4. **User Filtering**: Correct number of users exported based on configuration
5. **Data Format**: Valid JSON structure with required fields
6. **Password Hashes**: PBKDF2-SHA512 format with proper iteration count
7. **User IDs**: Valid GUID format
8. **Library Data**: Users with library access show populated libraries array with id, name, and type
9. **No Errors**: Clean log with no error messages

### ❌ Failure Indicators

1. **API Errors**: HTTP errors when triggering export
2. **Log Errors**: Error messages in export.log
3. **Missing Files**: Output files not created
4. **Invalid JSON**: Malformed JSON in users_basic.json
5. **Missing Data**: Required fields (id, username, passwordHash, libraries) missing
6. **Empty Libraries**: Users expected to have library access show empty libraries array
7. **Database Errors**: Unable to connect to Jellyfin database
8. **Filter Failures**: Wrong number of users exported

## Test Data Validation

### Current Test Instance
- **Total Users**: 4 (JellyFin, Family, Mitra, Chris)
- **Configured Export**: 2 users (Family, Mitra)
- **Expected Output**: 2 users in JSON file
- **Database Path**: `C:\ProgramData\Jellyfin\Server\data\jellyfin.db`
- **Export Directory**: `C:\Users\Sam\Documents\GitHub\Jellyfin-Migrator-Plugin\Exports\`

### Sample Expected Output

**Note**: Current output shows empty libraries arrays, which indicates either:
1. Users have no library permissions configured, or
2. Library export functionality needs to be implemented/fixed

```json
[
  {
    "id": "F2F1C240-79D7-43F1-A8C8-84BA7E63F1AB",
    "username": "Family",
    "passwordHash": "$PBKDF2-SHA512$iterations=210000$...",
    "libraries": [
      {
        "id": "LIBRARY-GUID-1",
        "name": "Movies",
        "type": "movies"
      },
      {
        "id": "LIBRARY-GUID-2",
        "name": "TV Shows",
        "type": "tvshows"
      }
    ]
  },
  {
    "id": "B8B1798D-F219-41BD-9FDC-DAFCE23097A3",
    "username": "Mitra",
    "passwordHash": "$PBKDF2-SHA512$iterations=210000$...",
    "libraries": [
      {
        "id": "LIBRARY-GUID-1",
        "name": "Movies",
        "type": "movies"
      }
    ]
  }
]
```

## Regression Testing

### After Code Changes

1. **Rebuild**: `./build.ps1 -Rebuild -Install -Restart`
2. **Re-test**: Run export via API
3. **Validate**: Check logs and output files
4. **Compare**: Ensure output format matches previous version

### Test Scenarios

1. **Basic Export**: Default configuration, selected users only
2. **All Users**: Configure to export all users
3. **Single User**: Export only one user
4. **No Users**: Test with no users selected (should handle gracefully)
5. **Database Issues**: Test with Jellyfin server down
6. **Permission Issues**: Test with invalid API key

## Troubleshooting

### Common Issues

1. **"Migration task not found"**: Plugin not installed or not enabled
2. **API key errors**: Invalid or expired API key
3. **Permission denied**: API key lacks sufficient permissions
4. **Database locked**: Jellyfin database in use
5. **Path issues**: Export directory not accessible

### Debug Steps

1. Check Jellyfin logs: `C:\ProgramData\Jellyfin\Server\logs\`
2. Verify plugin installation: Dashboard → Plugins
3. Test API connectivity: `GET /ScheduledTasks`
4. Check file permissions on export directory
5. Validate plugin configuration

## Automation

This test loop can be automated with continuous integration by:

1. Setting up test Jellyfin instance
2. Creating known test users
3. Running export via API
4. Validating output programmatically
5. Comparing results against expected baseline

The test ensures reliable user data migration functionality across plugin updates.
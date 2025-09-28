# Jellyfin Migrator Plugin - API Usage

This document describes how to start the migration export process programmatically using Jellyfin's API.

## Overview

The Jellyfin Migrator Plugin registers a scheduled task called `JellyfinMigratorExport` that can be triggered via API calls. This allows you to start the export process programmatically without using the web UI.

## Prerequisites

- Jellyfin Migrator Plugin installed and enabled
- Valid Jellyfin API key
- Plugin configuration saved (users selected, export directory set, etc.)

## API Method

### Step 1: Get Scheduled Tasks

Get the list of all scheduled tasks to find the migration export task:

```http
GET /ScheduledTasks
Authorization: MediaBrowser Token="5346b466a4ec42739071aa10671f22d4"
```

**Example Response:**
```json
[
  {
    "Id": "12345678-1234-1234-1234-123456789abc",
    "Name": "Migration: Export Data",
    "Key": "JellyfinMigratorExport",
    "Description": "Minimal export: users only (scoped down for reliability)",
    "Category": "Migration",
    "State": "Idle",
    "IsHidden": false,
    "IsEnabled": true
  }
]
```

### Step 2: Start the Export Task

Using the task ID from Step 1, start the export:

```http
POST /ScheduledTasks/Running/{taskId}
Authorization: MediaBrowser Token="your-api-key"
```

Replace `{taskId}` with the actual ID from the previous response.

## Complete Examples

### Using curl

```bash
# Step 1: Get all scheduled tasks
curl -H "Authorization: MediaBrowser Token=\"your-api-key\"" \
     http://your-jellyfin-server:8096/ScheduledTasks

# Step 2: Start the export task (replace {taskId} with actual ID)
curl -X POST \
     -H "Authorization: MediaBrowser Token=\"your-api-key\"" \
     http://your-jellyfin-server:8096/ScheduledTasks/Running/{taskId}
```

### Using PowerShell

```powershell
# Configuration
$server = "http://your-jellyfin-server:8096"
$apiKey = "your-api-key"
$headers = @{ Authorization = "MediaBrowser Token=`"$apiKey`"" }

# Step 1: Get scheduled tasks
$tasks = Invoke-RestMethod -Uri "$server/ScheduledTasks" -Headers $headers -Method Get

# Step 2: Find the migration task
$migrationTask = $tasks | Where-Object { $_.Key -eq "JellyfinMigratorExport" }

if ($migrationTask) {
    # Step 3: Start the task
    $taskId = $migrationTask.Id
    Invoke-RestMethod -Uri "$server/ScheduledTasks/Running/$taskId" -Headers $headers -Method Post
    Write-Host "Migration export started successfully"
} else {
    Write-Host "Migration task not found - ensure the plugin is installed"
}
```

### Using Python

```python
import requests

# Configuration
server = "http://your-jellyfin-server:8096"
api_key = "your-api-key"
headers = {"Authorization": f'MediaBrowser Token="{api_key}"'}

# Step 1: Get scheduled tasks
response = requests.get(f"{server}/ScheduledTasks", headers=headers)
tasks = response.json()

# Step 2: Find the migration task
migration_task = next((task for task in tasks if task.get("Key") == "JellyfinMigratorExport"), None)

if migration_task:
    # Step 3: Start the task
    task_id = migration_task["Id"]
    response = requests.post(f"{server}/ScheduledTasks/Running/{task_id}", headers=headers)

    if response.status_code == 204:
        print("Migration export started successfully")
    else:
        print(f"Failed to start migration: {response.status_code}")
else:
    print("Migration task not found - ensure the plugin is installed")
```

## Monitoring Progress

After starting the task, you can monitor its progress by periodically checking the task status:

```http
GET /ScheduledTasks
Authorization: MediaBrowser Token="your-api-key"
```

Look for the task with Key `JellyfinMigratorExport` and check its `State` field:
- `"Running"` - Export is in progress
- `"Idle"` - Export completed or not running
- `"Cancelling"` - Export is being cancelled

## Export Output

The export will create files in the configured export directory (or default location under Jellyfin data path). Current export includes:

- `users_basic.json` - User data with ID, username, password hashes, and library permissions
- `export.log` - Detailed log of the export process

## Configuration

The export uses the current plugin configuration. To modify what gets exported:

1. Go to Jellyfin Dashboard → Plugins → Migrator → Settings
2. Configure users, libraries, and export options
3. Save configuration
4. Run the API call to start export

## Error Handling

If the API call fails:
- Ensure the plugin is installed and enabled
- Verify the API key has sufficient permissions
- Check Jellyfin logs for detailed error messages
- Confirm the task ID is correct and the task exists

## Security Notes

- API keys should be kept secure and not exposed in logs
- The export may contain sensitive data (password hashes)
- Ensure export directory has appropriate access permissions
- Consider the security implications of automated exports
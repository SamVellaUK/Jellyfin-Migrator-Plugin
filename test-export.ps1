# Test Jellyfin Migration Export API
$server = "http://localhost:8096"
$apiKey = "5346b466a4ec42739071aa10671f22d4"
$headers = @{ Authorization = "MediaBrowser Token=`"$apiKey`"" }

try {
    Write-Host "Getting scheduled tasks..."
    $tasks = Invoke-RestMethod -Uri "$server/ScheduledTasks" -Headers $headers -Method Get

    # Find the migration task
    $migrationTask = $tasks | Where-Object { $_.Key -eq "JellyfinMigratorExport" }

    if ($migrationTask) {
        Write-Host "Found migration task: $($migrationTask.Name)"
        Write-Host "Task ID: $($migrationTask.Id)"
        Write-Host "Current State: $($migrationTask.State)"

        # Start the task
        Write-Host "Starting migration export..."
        $taskId = $migrationTask.Id
        Invoke-RestMethod -Uri "$server/ScheduledTasks/Running/$taskId" -Headers $headers -Method Post
        Write-Host "Migration export started successfully"
    } else {
        Write-Host "Migration task not found - listing all available tasks:"
        $tasks | ForEach-Object { Write-Host "- $($_.Name) (Key: $($_.Key))" }
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
    Write-Host "Make sure Jellyfin is running and the API key is correct"
}
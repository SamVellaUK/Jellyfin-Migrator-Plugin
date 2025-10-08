# How to Start a Library Scan and Poll for Completion in Jellyfin Plugins

When you add or update a library in Jellyfin, you typically want to trigger a scan so the server indexes the media files in the folder(s). For plugin developers, it's important not just to start the scan, but also to know **when it's finished** so you can continue with tasks like updating options or reporting status.

This guide shows you how to start a scan and poll for its completion, with simple code examples for junior developers.

---

## 1. Starting a Library Scan

Jellyfin exposes a method to scan libraries, usually available via the `ILibraryManager` interface.

### Example: Start a Scan by Library Name

```csharp
await libraryManager.ScanLibrary("Movies - Mitra");
```

You can also scan by **library ID** if you have it:

```csharp
// Assume libraryId is a Guid
var folder = libraryManager.GetItemById(libraryId) as Folder;
if (folder != null)
{
    await folder.ValidateChildren(progressReporter, cancellationToken);
}
```
---

## 2. Polling for Scan Completion

Jellyfin does not provide a direct "scan finished" event in the plugin API, but you can poll to check if the scan is complete.

### Method 1: Poll for Item Count Increase

After starting a scan, periodically check if the number of items in the library stops increasing.

```csharp
int lastCount = -1;
int stableCount = 0;
const int stableThreshold = 3; // Number of times count doesn't change

while (stableCount < stableThreshold)
{
    var folder = libraryManager.GetItemById(libraryId) as Folder;
    int currentCount = folder?.GetChildren().Count() ?? 0;

    if (currentCount == lastCount)
    {
        stableCount++;
    }
    else
    {
        stableCount = 0; // Reset if count changes
        lastCount = currentCount;
    }

    await Task.Delay(2000); // Wait 2 seconds before polling again
}
Console.WriteLine("Library scan appears to be complete.");
```

### Method 2: Use Scan Progress Events

Some versions of Jellyfin raise progress events you can subscribe to:

```csharp
libraryManager.LibraryScanProgressChanged += (sender, args) =>
{
    Console.WriteLine($"Scan progress: {args.Progress}%");
    if (args.Progress >= 100)
    {
        Console.WriteLine("Scan completed!");
    }
};
await libraryManager.ScanLibrary("Movies - Mitra");
```

**Note:** Not all plugin contexts support this event, so check your Jellyfin version.

---

## 3. Putting It Together

Here's a full example that starts a scan and polls for completion by checking the item count:

```csharp
public async Task ScanLibraryAndWaitAsync(ILibraryManager libraryManager, Guid libraryId)
{
    // Start scan
    var folder = libraryManager.GetItemById(libraryId) as Folder;
    if (folder == null)
    {
        Console.WriteLine("Library folder not found.");
        return;
    }

    var progress = new Progress<double>(percent => 
        Console.WriteLine($"Scan progress: {percent:F1}%"));

    await folder.ValidateChildren(progress, CancellationToken.None);

    // Poll for scan completion
    int lastCount = -1;
    int stableCount = 0;
    const int stableThreshold = 3;

    while (stableCount < stableThreshold)
    {
        int currentCount = folder.GetChildren().Count();
        if (currentCount == lastCount)
        {
            stableCount++;
        }
        else
        {
            stableCount = 0;
            lastCount = currentCount;
        }
        await Task.Delay(2000);
    }

    Console.WriteLine("Library scan appears to be complete.");
}
```

---

## 4. Tips

- Always check for nulls when working with folders and items.
- Use `CancellationToken` for long operations to allow cancellation.
- Adjust `stableThreshold` and delay to fit the size of your libraries.

---

## Summary

- **Start a scan** by name or ID.
- **Poll for completion** by monitoring progress or item counts.
- Wait until the scan is stable before proceeding.

This approach ensures your plugin waits for Jellyfin to finish indexing media before moving on to the next step.
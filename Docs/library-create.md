# How Plugins Can Add Libraries to a Jellyfin Instance

This guide explains how a Jellyfin plugin can programmatically add new libraries to a Jellyfin server. You'll learn how to create a library with a specific name, content type, and folder path(s), set optional attributes, and trigger a library scan, including waiting for it to finish. This is aimed at developers with a range of experience.

---

## Overview

A *library* in Jellyfin organizes media by content type and folder locations. Plugins can use Jellyfin's internal APIs to add libraries, configure them, and initiate scans. 

To add a library, you need to:
1. Create a library configuration (`LibraryOptions`).
2. Register the library via the `ILibraryManager`.
3. Start a scan using the `ILibraryManager`.
4. Optionally, wait for the scan to complete.

---

## Prerequisites

- Reference Jellyfin core libraries (usually via NuGet).
- Your plugin must run in the server context (not client-side).
- Basic understanding of dependency injection in Jellyfin plugins.

---

## 1. Creating a Library

You'll need access to Jellyfin's `ILibraryManager` and related types.

### Example: Adding a Movie Library

```csharp
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Configuration;

// Assume you have dependency injection set up for ILibraryManager and IFileSystem
public async Task AddMovieLibraryAsync(
    ILibraryManager libraryManager,
    IFileSystem fileSystem)
{
    var options = new LibraryOptions
    {
        Name = "My Movie Library",
        ContentType = LibraryContentType.Movies,
        // At least one folder path required
        FolderPaths = new[] { "/media/movies" },
        // Optional: Set other attributes
        EnableRealtimeMonitoring = true,
        EnableAutomaticRefresh = true,
        MetadataRefreshIntervalDays = 7
        // Add more options as needed
    };

    // Register the new library
    libraryManager.CreateLibrary(options);

    // Optionally, trigger an initial scan
    await libraryManager.ScanLibrary(options.Name);
}
```

---

## 2. Setting Optional Attributes

`LibraryOptions` exposes many attributes. Common ones include:

- `EnableRealtimeMonitoring` (bool): Watch for filesystem changes.
- `EnableAutomaticRefresh` (bool): Periodically refresh metadata.
- `MetadataRefreshIntervalDays` (int): Days between metadata refreshes.
- `ImageExtractionTimeoutMs` (int): How long to try extracting images.

### Example: Custom TV Library

```csharp
var options = new LibraryOptions
{
    Name = "TV Shows",
    ContentType = LibraryContentType.TvShows,
    FolderPaths = new[] { "/media/tv" },
    EnableRealtimeMonitoring = false,
    EnableAutomaticRefresh = true,
    MetadataRefreshIntervalDays = 14,
    ImageExtractionTimeoutMs = 30000
};
libraryManager.CreateLibrary(options);
```

---

## 3. Initiating a Library Scan

After creating the library, you'll want to scan the folders for media.

### Example: Start and Monitor Scan

```csharp
public async Task ScanAndWaitAsync(ILibraryManager libraryManager, string libraryName)
{
    var scanTask = libraryManager.ScanLibrary(libraryName);

    // Wait for the scan to complete
    await scanTask;
    Console.WriteLine($"Scan for '{libraryName}' completed.");
}
```

#### Handling Scan Progress

You may want to monitor progress or subscribe to events:

```csharp
libraryManager.LibraryScanProgressChanged += (s, e) =>
{
    Console.WriteLine($"Scan Progress: {e.Progress}%");
};
```

---

## 4. Complete Example: Plugin Service

```csharp
public class LibraryCreatorService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;

    public LibraryCreatorService(ILibraryManager libraryManager, IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
    }

    public async Task CreateAndScanLibraryAsync()
    {
        var options = new LibraryOptions
        {
            Name = "Documentaries",
            ContentType = LibraryContentType.Movies,
            FolderPaths = new[] { "/media/docs" },
            EnableRealtimeMonitoring = true
        };

        _libraryManager.CreateLibrary(options);

        // Start scan and wait for completion
        var scanTask = _libraryManager.ScanLibrary(options.Name);
        await scanTask;
    }
}
```

---

## 5. Notes and Best Practices

- **Folder paths must exist and be accessible** by the server.
- **Content type** should match the type of media (`Movies`, `TvShows`, `Music`, etc.).
- **Avoid duplicate names**: Library names must be unique.
- **Error handling**: Always handle exceptions when creating libraries or scanning.

### Example: Error Handling

```csharp
try
{
    _libraryManager.CreateLibrary(options);
    await _libraryManager.ScanLibrary(options.Name);
}
catch (Exception ex)
{
    // Log or handle the error appropriately
    Console.Error.WriteLine($"Failed to add library: {ex.Message}");
}
```

---

## 6. References

- [Jellyfin API Docs](https://jellyfin.org/docs/)
- [LibraryOptions Source](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Model/Configuration/LibraryOptions.cs)
- [ILibraryManager Interface](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Controller/Library/ILibraryManager.cs)

---

## Summary

Plugins can add libraries by creating a `LibraryOptions` object, registering it with `ILibraryManager`, and initiating a scan. You can configure all library attributes as needed and monitor scan progress. For more advanced use, consult the Jellyfin source code and plugin samples.

Happy coding!
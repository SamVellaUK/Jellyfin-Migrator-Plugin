# Why Is Library ID Null When Importing Libraries in Jellyfin Plugins?

When developing Jellyfin plugins that create or import libraries, you may encounter a situation where you can see your library in the UI or in code, but its unique **ID** is `null` or missing. This document explains why this happens, how Jellyfin manages library IDs, and how you can reliably obtain the ID after creation. This guide is aimed at junior developers, with simple code examples and troubleshooting tips.

---

## The Problem

**Scenario:**  
You create a new library (e.g., "Movies - Mitra") using Jellyfin's plugin API.  
You try to get its unique ID (used for updating settings, scanning, etc.) but the result is `null`.

**Log Example:**
```
Library 'Movies - Mitra' already exists with ID: 
VirtualFolder found: Name='Movies - Mitra', ItemId='(null)'
ERROR: Could not find library ID for 'Movies - Mitra' after all attempts
```

---

## Why Does This Happen?

Jellyfin's libraries are represented by "Virtual Folders." When you create a library, the underlying system needs to:
1. Register the library.
2. Initialize its database entry.
3. Scan the library folders for media.

Sometimes, the plugin code can see the new library by name, but the **ID is not ready yet** because:
- The scan hasn't run.
- The database entry isn't fully initialized.
- The system needs more time to finish setup.

This is a common race condition in Jellyfin plugin development.

---

## How Jellyfin Creates Libraries

**Typical Creation:**
```csharp
var options = new LibraryOptions {
    Name = "Movies - Mitra",
    PathInfos = new[] { new MediaPathInfo { Path = "/media/Movies Mitra" } }
};
libraryManager.AddVirtualFolder(options.Name, null, options, false);
```

**Trying to Get the ID:**
```csharp
var vf = libraryManager.GetVirtualFolders()
    .FirstOrDefault(f => f.Name == "Movies - Mitra");

string id = vf?.ItemId; // Often null immediately after creation!
```

---

## How to Fix: Reliable Library ID Retrieval

**Step 1. Trigger a Scan After Creating the Library**

This ensures Jellyfin finishes setting up the library.

```csharp
await libraryManager.ScanLibrary("Movies - Mitra");
```

**Step 2. Wait and Poll for the ID**

Add a delay and retry:

```csharp
Guid? GetLibraryIdByName(string name)
{
    var folders = libraryManager.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = new[] { BaseItemKind.CollectionFolder }
    });
    var found = folders.FirstOrDefault(f => f.Name == name);
    return found?.Id;
}

Guid? libraryId = null;
for (int i = 0; i < 10; i++)
{
    libraryId = GetLibraryIdByName("Movies - Mitra");
    if (libraryId != null) break;
    Thread.Sleep(2000); // Wait 2 seconds before retry
}
if (libraryId == null)
{
    Console.WriteLine("Failed to get library ID after retries!");
}
```

**Step 3. Only Proceed Once You Have the ID**

Do not try to update options or scan by ID until you have retrieved it.

---

## Full Example: Creating and Getting Library ID

```csharp
// 1. Create library
var options = new LibraryOptions {
    Name = "Movies - Mitra",
    PathInfos = new[] { new MediaPathInfo { Path = "/media/Movies Mitra" } }
};
libraryManager.AddVirtualFolder(options.Name, null, options, false);

// 2. Trigger scan
await libraryManager.ScanLibrary(options.Name);

// 3. Poll for ID
Guid? libraryId = null;
for (int i = 0; i < 10; i++)
{
    var folders = libraryManager.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = new[] { BaseItemKind.CollectionFolder }
    });
    var found = folders.FirstOrDefault(f => f.Name == options.Name);
    if (found != null)
    {
        libraryId = found.Id;
        break;
    }
    await Task.Delay(2000); // Wait 2 seconds before retry
}
if (libraryId == null)
{
    Console.WriteLine("Failed to get library ID after retries!");
}
else
{
    Console.WriteLine($"Library created! ID: {libraryId}");
}
```

---

## Troubleshooting Tips

- **Always scan the library after creation.** The scan makes sure Jellyfin finishes setup.
- **Use a retry loop** with delays to poll for the library ID.
- **Log available libraries and their IDs** to help debug.
- If you still can't get the ID, check Jellyfin server logs for errors or permission issues.

---

## Summary

- Jellyfin may not assign an ID to a new library until after the first scan.
- Always scan and poll for the ID after creating a library.
- Only proceed with further operations once you have a valid ID.

---

**Further Reading:**
- [Jellyfin plugin development docs](https://jellyfin.org/docs/)
- [Jellyfin source code - LibraryManager](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Controller/Library/ILibraryManager.cs)

Happy coding!
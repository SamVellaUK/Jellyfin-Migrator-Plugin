# Jellyfin Plugins & User Services: A Practical Guide for Migration Developers

> **Goal:** Help you write a **custom Jellyfin server plugin** that can **read and export user data** (accounts, per-user watch-state, progress, ratings, favorites) by referencing Jellyfin’s internal services.

> **Audience:** Migration developers moving user data **out of** Jellyfin (e.g., to another system).

---

## TL;DR

* Jellyfin plugins are .NET assemblies loaded by the server. They use **constructor injection** to access core services.
* The two key user-facing services are:

  * `IUserManager` – enumerate and fetch users and user-related info. ([GitHub][1])
  * `IUserDataManager` – read/write **per-user, per-item** data (played state, progress, favorites, ratings). ([GitLab][2])
* The official **plugin template** shows how to reference these services and register your own. ([GitHub][1])
* If you need to cross-check public API shapes (for testing JSON you emit), Jellyfin’s **OpenAPI** listing is available. ([api.jellyfin.org][3])
* For ground truth on where Jellyfin stores local user accounts on disk (useful for validation), see docs & forum notes. ([jellyfin.org][4])

---

## 1) How Plugins Get Access to Internal Services

Jellyfin uses ASP.NET Core–style **Dependency Injection**. When your plugin class (or your controllers / hosted services) request an interface in the constructor, Jellyfin injects the concrete implementation at runtime.

* The **Plugin Template** documents this pattern and explicitly lists commonly used services, including `IUserManager`. It also explains `IPluginServiceRegistrator` for registering your own services. ([GitHub][1])

### Minimal plugin scaffolding (what you need)

* Reference the public packages:

  * `Jellyfin.Model`
  * `Jellyfin.Controller`
    (The template shows the exact package names/targets.) ([GitHub][1])

* Implement a `Plugin` class deriving from `BasePlugin<TConfig>` and (optionally) an `IPluginServiceRegistrator` to add your own services to DI. ([GitHub][1])

---

## 2) The User Services You’ll Use

### `IUserManager`

Gives you access to **user accounts** stored by the server. Use it to enumerate users, look them up by Id/Name, and inspect user-level settings and permissions.

* The template explicitly calls out **`IUserManager – Allows you to retrieve user info and user library related info`**. ([GitHub][1])
* Core server controllers (e.g., `UserController`) are implemented around an injected `_userManager`, which indicates you’ll follow the same pattern in your plugin. ([GitLab][5])

Common things you’ll need for export:

* **User Id** (GUID),
* **User Name / Display Name**,
* **Permissions / Policy** (e.g., parental controls, admin flag) if you need to migrate policies.

> Tip: If your export format mirrors Jellyfin’s API DTOs, you can model your JSON on the public OpenAPI schema for user records to keep things compatible. ([api.jellyfin.org][3])

---

### `IUserDataManager`

Handles **per-user, per-item** data (what most migrations care about):

* **Played state / Play count**
* **Playback position** (“resume point”)
* **IsFavorite / Like / User rating**
* **Last played date** and related timestamps

This interface is part of `MediaBrowser.Controller.Library` in the server codebase; it’s the canonical provider for reading/updating user item data. ([GitLab][2])

> Practical note: community reports and issues frequently discuss fields like `UserData.Played`, `PlayCount`, and last-played timestamps—use these to map to your destination system. ([GitHub][6])

---

## 3) Typical Injection Pattern (Controller or Task)

Create a REST controller in your plugin (or a scheduled/background task) and request the services you need via the constructor:

```csharp
using MediaBrowser.Controller.Library; // IUserDataManager, ILibraryManager
using MediaBrowser.Controller.Users;   // IUserManager
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("Plugins/UserExport")]
public class UserExportController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userData;
    private readonly ILibraryManager _library;

    public UserExportController(
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager)
    {
        _userManager = userManager;
        _userData = userDataManager;
        _library = libraryManager;
    }

    [HttpGet("all")]
    public IActionResult ExportAllUserData()
    {
        // enumerate users (_userManager)
        // enumerate library items (_library)
        // fetch per-user item data (_userData)
        // transform to your export JSON
        // return File(...) or JSON result
        return Ok(/* your payload */);
    }
}
```

**Why this works:** Jellyfin finds your controller, injects `IUserManager` and `IUserDataManager`, and you call them directly. The plugin template recommends exactly this approach (constructor injection into plugin classes and controllers). ([GitHub][1])

---

## 4) A Concrete Export Recipe

Below is a safe sequence that covers the main migration requirements:

1. **Enumerate users**

   * Use `IUserManager` to list users and capture at least: **Id**, **Name**, and, if needed, **policy/permissions**. ([GitHub][1])

2. **Collect the item universe**

   * Use `ILibraryManager` to iterate relevant media items (Movies, Episodes, etc.) and note their **Item Ids**. (This is the stable join key for per-user data.) The plugin template lists `ILibraryManager` for direct library access. ([GitHub][1])

3. **Per-user, per-item user data**

   * For each `(User, Item)` pair, use `IUserDataManager` to fetch:

     * `Played` (boolean)
     * `PlayCount` (int)
     * `PlaybackPositionTicks` / resume point (long)
     * `IsFavorite` (boolean)
     * `Rating`/**Like** (nullable)
     * `LastPlayedDate` (datetime)
       These are the fields migration targets care about; they’re commonly referenced in community/API discussions and backed by `IUserDataManager` in the server. ([GitLab][2])

4. **Serialize**

   * Emit a **flat JSON** array (`UserId`, `ItemId`, fields above), or group by user.
   * If you want to mirror the **server’s REST** shapes, consult the OpenAPI index for current DTOs and naming. ([api.jellyfin.org][3])

5. **Expose an endpoint or write a file**

   * Add a `ControllerBase` endpoint that streams JSON to the caller (admin) or
   * Use `IServerApplicationPaths` to write a file under Jellyfin’s data path (admin later downloads it). The template lists this service as available. ([GitHub][1])

---

## 5) Security & Permissions

* Protect your export controller:

  * Require **administrator** context (e.g., attribute or manual check using your injected `IUserManager` / policy) before allowing export.
* If you must test outside the plugin, the **public API** auth header format and keys are documented/queried often; see API docs & notes. ([GitHub][7])

---

## 6) Version Notes & Where the Truth Lives

* Jellyfin server continues to evolve (EFCore rewrite landed around 10.11 timeframe; backup docs reference an official future backup/restore plugin). Your plugin should avoid hard-coding DB layout—use **services** (`IUserManager`, `IUserDataManager`) to stay version-robust. ([jellyfin.org][4])
* **Data-at-rest** locations (useful for sanity checks/backups while testing):

  * Linux packages: `/var/lib/jellyfin` for data; users reside in `.../data/jellyfin.db`.
  * Windows installer: `C:\ProgramData\Jellyfin\Server` by default.
    Official docs list per-platform paths, and forum posts confirm `jellyfin.db` stores user accounts. ([jellyfin.org][4])

---

## 7) Quick Start Checklist

* [ ] Clone the **plugin template** and build. ([GitHub][1])
* [ ] Add references to `Jellyfin.Model` and `Jellyfin.Controller`. ([GitHub][1])
* [ ] Add a controller (or scheduled task) that injects `IUserManager`, `IUserDataManager`, `ILibraryManager`. ([GitHub][1])
* [ ] Implement export logic and return JSON/file.
* [ ] Test against current **OpenAPI** to validate shapes/names. ([api.jellyfin.org][3])
* [ ] Secure the endpoint (admin only).
* [ ] Package the DLL in `/plugins/<YourPlugin>` and restart server (template includes tips). ([GitHub][1])

---

## 8) Handy References

* **Plugin Template** – how to wire up DI, register services, and what’s available (lists `IUserManager`, `ILibraryManager`, etc.). ([GitHub][1])
* **OpenAPI index** – current server API surface (useful for testing your exported JSON against public shapes). ([api.jellyfin.org][3])
* **User management docs** – admin/UI view of users. ([jellyfin.org][8])
* **Backup/Restore & data paths** – where Jellyfin stores its data on disk (for verification during testing). ([jellyfin.org][4])
* **Users DB location (community confirmation)** – `.../data/jellyfin.db` contains accounts. ([Jellyfin Forum][9])

---

## 9) Example Export Shape (suggested)

```json
{
  "exportedAt": "2025-10-05T12:00:00Z",
  "server": { "version": "10.x", "id": "..." },
  "users": [
    {
      "userId": "GUID",
      "name": "alice",
      "policy": { "isAdministrator": true },
      "items": [
        {
          "itemId": "GUID",
          "played": true,
          "playCount": 3,
          "playbackPositionTicks": 5320000000,
          "isFavorite": false,
          "userRating": 8,
          "lastPlayedDate": "2025-09-01T20:15:00Z"
        }
      ]
    }
  ]
}
```

This keeps things **simple and lossless** for typical migrations. Extend as needed.

---

### Final advice

* **Prefer services** over DB queries: your code survives Jellyfin upgrades. (`IUserManager`/`IUserDataManager` are the supported seam.) ([GitHub][1])
* Treat the **OpenAPI** as a compatibility oracle for payload shapes. ([api.jellyfin.org][3])
* Keep an eye on release notes around **10.11+** due to the EFCore migration (export via services should remain stable, but verify). ([jellyfin.org][4])

Happy exporting!

[1]: https://github.com/jellyfin/jellyfin-plugin-template "GitHub - jellyfin/jellyfin-plugin-template: Plugin Template for Jellyfin"
[2]: https://git.shivering-isles.com/github-mirror/jellyfin/jellyfin/-/blob/v10.7.5/MediaBrowser.Controller/Library/IUserDataManager.cs?ref_type=tags&utm_source=chatgpt.com "MediaBrowser.Controller/Library/IUserDataManager.cs · v10.7.5 ..."
[3]: https://api.jellyfin.org/openapi/ "Index of /openapi/"
[4]: https://jellyfin.org/docs/general/administration/backup-and-restore/ "Backup and Restore | Jellyfin"
[5]: https://git.shivering-isles.com/github-mirror/jellyfin/jellyfin/-/blob/v10.9.10/Jellyfin.Api/Controllers/UserController.cs?utm_source=chatgpt.com "Jellyfin.Api/Controllers/UserController.cs · v10.9.10 ... - GitLab"
[6]: https://github.com/jellyfin/jellyfin/issues/14351?utm_source=chatgpt.com "Played status is reset · Issue #14351 · jellyfin ..."
[7]: https://github.com/jellyfin/jellyfin/issues/12990?utm_source=chatgpt.com "API Authentication documentation incorrect · Issue #12990"
[8]: https://jellyfin.org/docs/general/server/users/adding-managing-users/?utm_source=chatgpt.com "Managing Users"
[9]: https://forum.jellyfin.org/t-where-jellyfin-stores-users?utm_source=chatgpt.com "where jellyfin stores users"

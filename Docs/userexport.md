# Expanding Jellyfin User Export to Include Common Attributes

This guide describes how to enrich the user export functionality in your Jellyfin plugin to include more user attributes. The context is based on the example `ExportService` and `UserExporter` classes, which currently export basic user information such as `id`, `username`, and accessible libraries.

**Audience:**  
Experienced developers who are new to the Jellyfin API and want to build a more comprehensive user export.

---

## 1. Understanding the Current Export

Currently, the export writes out a list of objects with these fields:

- `id`: User's GUID (no dashes)
- `username`: User's login name
- `passwordHash`: Password hash from the database (if available)
- `libraries`: List of libraries the user can access

Example:
```json
{
  "id": "9dcfcd1b9de04c66b7e2be56ad6243b7",
  "username": "alice",
  "passwordHash": "AQAAAAEA...",
  "libraries": [
    { "id": "e3c1d8e...", "name": "Movies" }
  ]
}
```

---

## 2. Common Jellyfin User Attributes

Jellyfin exposes user data via the [`IUser`](https://github.com/jellyfin/jellyfin/blob/next/MediaBrowser.Controller/Entities/IUser.cs) interface and related classes.  
Common attributes include:

- **User ID** and **Username**
- **IsAdministrator**: Whether the user is an admin
- **IsDisabled**: Whether the user account is disabled
- **IsHidden**: If the user is hidden from the login screen
- **Last Login Date**
- **Configuration**: User-specific settings (subtitle language, theme, etc.)
- **Policy**: Permissions (parental controls, library access, etc.)
- **Display Preferences**
- **Authentication info**: e.g., `PasswordResetProviderId`, `HasPassword`

These can be accessed via the `IUser` object and its methods/properties.

---

## 3. How to Expand the Export

### a) Accessing More Properties

Within your `UserExporter`, after you get a user from `_userManager.Users`, you can access many more properties:

```csharp
foreach (var user in allUsers)
{
    var exportObj = new
    {
        id = user.Id.ToString("N"),
        username = user.Username,
        isAdministrator = user.HasPermission(PermissionKind.IsAdministrator),
        isDisabled = user.HasPermission(PermissionKind.IsDisabled),
        isHidden = user.HasPermission(PermissionKind.IsHidden),
        lastLoginDate = user.LastLoginDate,
        passwordHash = passwordHashes.TryGetValue(user.Username, out var hash) ? hash : null,
        configuration = user.Configuration, // UserConfiguration object
        policy = user.Policy,               // UserPolicy object
        libraries = ... // as before
    };
    users.Add(exportObj);
}
```

#### Note:
- `user.Configuration` and `user.Policy` are complex objects; you may want to flatten or select key fields.
- `HasPermission(PermissionKind.XXX)` is the standard way to check roles and states.

---

### b) Including User Configuration & Policy

Both `UserConfiguration` and `UserPolicy` have many useful properties (e.g., preferred language, max parental rating). You can export the full objects or select key fields:

```csharp
configuration = new {
    user.Configuration.AudioLanguagePreference,
    user.Configuration.SubtitleLanguagePreference,
    user.Configuration.Theme
    // ...add more as needed
},
policy = new {
    user.Policy.IsAdministrator,
    user.Policy.EnableAllFolders,
    user.Policy.BlockedTags,
    user.Policy.MaxParentalRating
    // ...add more as needed
}
```

---

### c) Example: Expanded User Export Object

Your export can now look like this:

```json
{
  "id": "9dcfcd1b9de04c66b7e2be56ad6243b7",
  "username": "alice",
  "isAdministrator": true,
  "isDisabled": false,
  "isHidden": false,
  "lastLoginDate": "2024-08-19T19:54:44Z",
  "configuration": {
    "audioLanguagePreference": "eng",
    "subtitleLanguagePreference": "eng",
    "theme": "Dark"
  },
  "policy": {
    "enableAllFolders": true,
    "blockedTags": [],
    "maxParentalRating": 9
  },
  "passwordHash": "AQAAAAEA...",
  "libraries": [
    { "id": "e3c1d8e...", "name": "Movies" }
  ]
}
```

---

## 4. Updating Your Export Code

Update the `GetUsers` method in `UserExporter.cs` accordingly:

```csharp
private List<object> GetUsers(...)
{
    ...
    foreach (var user in allUsers)
    {
        ...
        users.Add(new {
            id,
            username,
            isAdministrator = user.HasPermission(PermissionKind.IsAdministrator),
            isDisabled = user.HasPermission(PermissionKind.IsDisabled),
            isHidden = user.HasPermission(PermissionKind.IsHidden),
            lastLoginDate = user.LastLoginDate,
            configuration = user.Configuration,
            policy = user.Policy,
            passwordHash,
            libraries
        });
    }
    ...
}
```

If you need to flatten or selectively include fields, do so by creating new objects for `configuration` and `policy`.

---

## 5. Reference: Common IUser Properties

- `Id` (Guid)
- `Username` (string)
- `LastLoginDate` (DateTime?)
- `HasPermission(PermissionKind)` (bool)
- `Configuration` (`UserConfiguration`)
- `Policy` (`UserPolicy`)
- `DisplayPreferencesId` (Guid)
- `HasPassword` (bool)
- `PasswordResetProviderId` (string)

See Jellyfin's [IUser interface](https://github.com/jellyfin/jellyfin/blob/next/MediaBrowser.Controller/Entities/IUser.cs) for full details.

---

## 6. Testing and Validation

- Use a test Jellyfin instance with multiple users and roles.
- Export and inspect the resulting JSON to ensure all required attributes appear.
- Consider privacy: **never export sensitive data unless you have user/admin consent**.

---

## 7. Further Enhancements

- Export linked devices, API keys, or playback history (requires additional queries/managers)
- Support filtering/sanitizing output for GDPR or other compliance
- Provide CLI or UI options for selecting which fields to export

---

## Summary

Expanding the user export is straightforward: access more properties from the `IUser` object, and include them in your export object.  
Jellyfin's API and data model are flexible and allow you to tailor exports to your requirements.

---
**Happy coding!**
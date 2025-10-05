# Recommended Core User Attributes for Jellyfin System Migration

When migrating a Jellyfin system, exporting the right set of user attributes is crucial for preserving user accounts, permissions, and preferences in the destination environment. Below is a recommended core set of user attributes to export as part of a system migration.

---

## 1. User Identification

- **id**: Unique user identifier (GUID, string, normalized)
- **username**: Login name for the user
- **primaryImageTag**: (optional) Avatar/profile image tag if present

---

## 2. Authentication

- **passwordHash**: (string, if accessible and permitted)  
  *Note: Only export this if you are migrating to another Jellyfin instance and have appropriate permissions. Omit for security if not required.*

---

## 3. User Roles & Status

- **isAdministrator**: (bool) – Whether the user is an administrator
- **isDisabled**: (bool) – If the account is currently disabled
- **isHidden**: (bool) – Whether user appears on login screens
- **lastLoginDate**: (DateTime, nullable) – Last login timestamp

---

## 4. Policy & Permissions

- **policy**: (object, or select fields below)
  - **isAdministrator**: (bool)
  - **enableAllFolders**: (bool)
  - **blockedTags**: (array of strings)
  - **maxParentalRating**: (int)
  - **enableRemoteAccess**: (bool)
  - **allowedFolders**: (array of GUIDs or names)

---

## 5. Configuration & Preferences

- **configuration**: (object, or select fields below)
  - **audioLanguagePreference**: (string)
  - **subtitleLanguagePreference**: (string)
  - **theme**: (string)
  - **displayMissingEpisodes**: (bool)
  - **orderedViews**: (array – user’s library order)
  - **enableNextEpisodeAutoPlay**: (bool)

---

## 6. Library Access

- **libraries**: (array of objects)
  - **id**: (GUID, normalized)
  - **name**: (string)

---

## 7. Additional (Optional)

- **playbackHistory**: (array or summary; useful for restoring watch states)
- **displayPreferencesId**: (GUID; for advanced scenarios)

---

## Example Export Object

```json
{
  "id": "9dcfcd1b9de04c66b7e2be56ad6243b7",
  "username": "alice",
  "isAdministrator": true,
  "isDisabled": false,
  "isHidden": false,
  "lastLoginDate": "2025-10-05T14:54:00Z",
  "policy": {
    "enableAllFolders": true,
    "blockedTags": [],
    "maxParentalRating": 9,
    "enableRemoteAccess": true,
    "allowedFolders": ["e3c1d8e..."]
  },
  "configuration": {
    "audioLanguagePreference": "eng",
    "subtitleLanguagePreference": "eng",
    "theme": "Dark",
    "displayMissingEpisodes": false,
    "enableNextEpisodeAutoPlay": true
  },
  "libraries": [
    { "id": "e3c1d8e...", "name": "Movies" },
    { "id": "c2f3a213...", "name": "TV Shows" }
  ]
}
```

---

## Notes & Best Practices

- **Do not export sensitive info (e.g., passwordHash) unless absolutely required and with explicit permission.**
- If migrating between Jellyfin instances, including policy and configuration fields ensures a seamless experience for users.
- Library access is essential for restoring permissions and restrictions.

---

**Tip:**  
You can expand this core set with additional fields based on your specific migration requirements or organizational policies.
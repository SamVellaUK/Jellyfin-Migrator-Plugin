To assign library access to a user via a plugin in Jellyfin, the process involves working with the user's policy and the library manager interfaces. Here’s a step-by-step walkthrough based on the repository’s code structure:

---

### 1. Access the User’s Policy

- **UserPolicy** (`MediaBrowser.Model/Users/UserPolicy.cs`) contains properties like `EnableAllFolders` and `EnabledFolders` (array of library/folder GUIDs) that determine which libraries the user can access.
    ```c#
    public class UserPolicy
    {
        public bool EnableAllFolders { get; set; }
        public Guid[] EnabledFolders { get; set; }
        // ... other properties
    }
    ```

---

### 2. Plugin Code: Modifying User Access

- A plugin, typically inheriting from `BasePlugin`, would interact with the user management system through dependency injection or service location.
- To change access, you modify the relevant user’s `UserPolicy`:

    ```c#
    // Get user manager from DI/service locator
    IUserManager userManager = ...;

    // Get the user (by ID, name, etc.)
    User user = userManager.GetUserById(userId);

    // Modify their access policy
    user.Policy.EnableAllFolders = false;
    user.Policy.EnabledFolders = new[] { libraryGuid1, libraryGuid2 };

    // Save the changes
    await userManager.UpdatePolicyAsync(user.Id, user.Policy);
    ```

    - `UpdatePolicyAsync` (`IUserManager.cs`) persists the user’s policy changes.

---

### 3. How Plugins Fit In

- Plugins can expose UI/configuration for admins to select which users get access to which libraries.
- The plugin will use the API or backend services to:
    - List libraries (from `ILibraryManager`)
    - List users (from `IUserManager`)
    - Update a user’s policy as above.

---

### 4. Example UI/Config Integration

- Plugins may provide configuration pages (see examples like `config.html` in plugin folders).
- The backend logic will trigger the policy update based on admin choice.

---

### 5. Summary

- **Assigning library access** is done by editing `UserPolicy.EnabledFolders` for the user.
- Use `IUserManager.UpdatePolicyAsync` to persist changes.
- Plugins act as orchestrators, providing UI and logic to modify these policies.

---

If you want a sample code snippet or more details about the plugin integration points, let me know!
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Export;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Import;

/// <summary>
/// Imports users from the analyzed export, restores library permissions,
/// and updates password hashes in the SQLite DB if present.
/// </summary>
internal sealed class UserImporter
{
    private readonly IApplicationPaths _paths;
    private readonly ExportLogger _exportLogger;
    private readonly ILogger _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;

    internal UserImporter(
        IApplicationPaths paths,
        ExportLogger exportLogger,
        ILogger logger,
        IUserManager userManager,
        ILibraryManager libraryManager)
    {
        _paths = paths;
        _exportLogger = exportLogger;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Import users after libraries have been rebuilt.
    /// </summary>
    /// <param name="extractedPath">Extracted export path (contains users_basic.json).</param>
    /// <param name="users">Users parsed during analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of users created.</returns>
    internal async Task<int> ImportUsersAsync(
        string extractedPath,
        List<ImportService.ImportUser> users,
        CancellationToken cancellationToken)
    {
        _exportLogger.Log("=== User Import Started (after library rebuild) ===");

        // Build current library name -> Guid map (use current server state)
        var nameToId = GetCurrentLibraryNameMap();
        _exportLogger.Log($"Found {nameToId.Count} libraries on destination to map permissions");

        var createdCount = 0;

        foreach (var u in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var username = (u.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                _exportLogger.Log("Skipping user with empty username");
                continue;
            }

            try
            {
                // Check conflict
                if (_userManager.Users.Any(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)))
                {
                    _exportLogger.Log($"Warning: User '{username}' already exists. Skipping creation and not overwriting.");
                    continue;
                }

                // Create the user (try common signatures dynamically for compatibility)
                var created = await CreateUserDynamicAsync(username, cancellationToken).ConfigureAwait(false);
                if (created == null)
                {
                    _exportLogger.Log($"Failed to create user '{username}' (unknown API). Skipping.");
                    continue;
                }

                createdCount++;
                try
                {
                    var idVal = created?.GetType().GetProperty("Id")?.GetValue(created)?.ToString();
                    _exportLogger.Log($"User created: {username}{(string.IsNullOrEmpty(idVal) ? string.Empty : " (" + idVal + ")")}");
                }
                catch
                {
                    _exportLogger.Log($"User created: {username}");
                }

                // Restore library permissions (disable all folders and assign allowed ones)
                await RestoreLibraryPermissionsAsync(created!, u, nameToId, cancellationToken).ConfigureAwait(false);

                // Update password hash in DB if present
                if (!string.IsNullOrWhiteSpace(u.PasswordHash))
                {
                    TryUpdatePasswordHash(username, u.PasswordHash!);
                }
            }
            catch (OperationCanceledException)
            {
                _exportLogger.Log("User import cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _exportLogger.LogError($"Failed to import user '{username}': {ex.Message}", ex);
            }
        }

        _exportLogger.Log($"=== User Import Completed: {createdCount}/{users.Count} created (existing users skipped) ===");
        return createdCount;
    }

    private Dictionary<string, Guid> GetCurrentLibraryNameMap()
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Use virtual folders exposed by ILibraryManager
        foreach (var vf in _libraryManager.GetVirtualFolders())
        {
            if (!string.IsNullOrWhiteSpace(vf?.Name) && Guid.TryParse(vf?.ItemId, out var id))
            {
                map[vf.Name] = id;
            }
        }

        return map;
    }

    private async Task<object?> CreateUserDynamicAsync(string username, CancellationToken cancellationToken)
    {
        // Attempt a variety of known signatures via reflection to be resilient across Jellyfin versions.
        try
        {
            var manager = _userManager;
            var type = manager.GetType();

            // Candidate method names in probable order
            var names = new[] { "CreateUserAsync", "AddUserAsync", "CreateUser", "AddUser" };
            foreach (var name in names)
            {
                var methods = type.GetMethods().Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).ToList();
                foreach (var m in methods)
                {
                    try
                    {
                        var args = BuildArgsForUserCreate(m.GetParameters(), username, cancellationToken, out bool isAsync, out bool needsAwait);
                        if (args == null)
                        {
                            continue;
                        }

                        var result = m.Invoke(manager, args);
                        if (needsAwait && result is Task t)
                        {
                            await t.ConfigureAwait(false);
                            // Try to get Task<T>.Result if present
                            var resultProp = t.GetType().GetProperty("Result");
                            result = resultProp?.GetValue(t);
                            if (result == null)
                            {
                                // Method may have been Task without result; fetch the user by name
                                try
                                {
                                    result = _userManager.GetUserByName(username);
                                }
                                catch
                                {
                                    // ignore
                                }
                            }
                        }

                        if (result != null)
                        {
                            return result;
                        }
                        else
                        {
                            // If void or null result, try to fetch user
                            try
                            {
                                var fetched = _userManager.GetUserByName(username);
                                if (fetched != null)
                                {
                                    return fetched;
                                }
                            }
                            catch
                            {
                                // ignore
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _exportLogger.Log($"User create attempt {m.Name} failed: {ex.Message}");
                        // Try next overload
                    }
                }
            }

            // Fallback: some servers may auto-create on first reference; try GetUserByName
            try
            {
                var existing = _userManager.GetUserByName(username);
                return existing;
            }
            catch
            {
                // ignore
            }
        }
        catch (Exception ex)
        {
            _exportLogger.Log($"CreateUser reflection failed: {ex.Message}");
        }

        return null;
    }

    private static object?[]? BuildArgsForUserCreate(System.Reflection.ParameterInfo[] parameters, string username, CancellationToken ct, out bool isAsync, out bool needsAwait)
    {
        isAsync = false;
        needsAwait = false;
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var pn = (p.Name ?? string.Empty).ToLowerInvariant();
            var pt = p.ParameterType;

            if (pt == typeof(string))
            {
                if (pn.Contains("user", StringComparison.Ordinal) || pn.Contains("name", StringComparison.Ordinal))
                {
                    args[i] = username;
                }
                else if (pn.Contains("pass", StringComparison.Ordinal))
                {
                    args[i] = string.Empty; // set blank password; user can reset later
                }
                else
                {
                    args[i] = string.Empty;
                }
            }
            else if (pt == typeof(bool))
            {
                // Default sensible flags; do not grant admin by default
                args[i] = false;
            }
            else if (pt == typeof(CancellationToken))
            {
                args[i] = ct;
            }
            else
            {
                // Attempt default of value types or null for refs
                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
        }

        // Identify whether method is async Task / Task<T>
        var returnsTask = parameters.Length >= 0; // placeholder
        // Caller will set properly by inspecting return value type
        needsAwait = true; // we will set to true when actually invoking and detecting Task
        isAsync = true;
        return args;
    }

    private async Task RestoreLibraryPermissionsAsync(object user, ImportService.ImportUser source, Dictionary<string, Guid> nameToId, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var username = (string?)user.GetType().GetProperty("Username")?.GetValue(user) ?? "?";

        _exportLogger.Log($"");
        _exportLogger.Log($"========================================");
        _exportLogger.Log($"User '{username}': Analyzing library permissions");
        _exportLogger.Log($"========================================");

        // Extract library names from export JSON
        var requestedNames = (source.LibraryNames ?? new List<string>())
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToList();

        _exportLogger.Log($"");
        _exportLogger.Log($"📋 Libraries from export JSON:");
        if (requestedNames.Count == 0)
        {
            _exportLogger.Log($"   (none)");
        }
        else
        {
            foreach (var name in requestedNames)
            {
                _exportLogger.Log($"   - '{name}'");
            }
        }

        _exportLogger.Log($"");
        _exportLogger.Log($"📚 Libraries available on destination server:");
        if (nameToId.Count == 0)
        {
            _exportLogger.Log($"   (none)");
        }
        else
        {
            foreach (var kvp in nameToId)
            {
                _exportLogger.Log($"   - '{kvp.Key}' -> {kvp.Value}");
            }
        }

        _exportLogger.Log($"");
        _exportLogger.Log($"🔍 Matching libraries:");
        var matched = new List<(string Name, Guid Id)>();
        var missing = new List<string>();

        foreach (var name in requestedNames)
        {
            if (nameToId.TryGetValue(name, out var id))
            {
                matched.Add((name, id));
                _exportLogger.Log($"   ✓ MATCH: '{name}' -> {id}");
            }
            else
            {
                missing.Add(name);
                _exportLogger.Log($"   ✗ NOT FOUND: '{name}'");
            }
        }

        _exportLogger.Log($"");
        _exportLogger.Log($"📊 Summary:");
        _exportLogger.Log($"   Requested: {requestedNames.Count}");
        _exportLogger.Log($"   Matched: {matched.Count}");
        _exportLogger.Log($"   Missing: {missing.Count}");
        _exportLogger.Log($"========================================");

        // Update user permissions if we have matches
        if (matched.Count > 0)
        {
            try
            {
                // Get user ID as Guid
                var userIdValue = user.GetType().GetProperty("Id")?.GetValue(user);
                if (userIdValue == null)
                {
                    _exportLogger.Log($"❌ ERROR: Could not get user ID");
                    return;
                }

                Guid userGuid;
                if (userIdValue is Guid guid)
                {
                    userGuid = guid;
                }
                else if (!Guid.TryParse(userIdValue.ToString(), out userGuid))
                {
                    _exportLogger.Log($"❌ ERROR: Invalid user ID format: {userIdValue}");
                    return;
                }

                _exportLogger.Log($"");
                _exportLogger.Log($"💾 Updating user permissions...");
                _exportLogger.Log($"   User ID: {userGuid}");

                // Get the policy from the newly created user object (it exists!)
                var policyProp = user.GetType().GetProperty("Policy");
                var policy = policyProp?.GetValue(user) as UserPolicy;

                if (policy != null)
                {
                    _exportLogger.Log($"❌ ERROR: User policy is unexpectedly non-null on created user");
                    return;
                }
                else
                {
                    _exportLogger.Log($"New User - Null policy as expected, creating new policy object");

                    _exportLogger.Log($"Creating user policy from scratch...");

                    var libraryGuids = matched.Select(m => m.Id).ToArray();

                    _exportLogger.Log($"   Setting EnableAllFolders: false");
                    _exportLogger.Log($"   Setting EnabledFolders to: {string.Join(", ", libraryGuids.Select(g => g.ToString()))}");

                    // Create new policy object
                    policy = new UserPolicy
                    {
                        EnableAllFolders = false,
                        EnabledFolders = libraryGuids
                    };

                    // _exportLogger.Log($"   Retrieved policy from user object");
                    _exportLogger.Log($"   Current EnableAllFolders: {policy.EnableAllFolders}");
                    _exportLogger.Log($"   Current EnabledFolders: {policy.EnabledFolders?.Length ?? 0}");

                    await _userManager.UpdatePolicyAsync(userGuid, policy).ConfigureAwait(false);
                }

                _exportLogger.Log($"✅ Permission update completed successfully");
            }
            catch (Exception ex)
            {
                _exportLogger.Log($"❌ ERROR updating permissions: {ex.Message}");
                _exportLogger.LogError($"Stack trace: {ex.StackTrace}", ex);
            }
        }
        else
        {
            try
            {
                _exportLogger.Log($"");
                _exportLogger.Log($"⚠️  No matched libraries - skipping individual permission update");
                _exportLogger.Log($"   Setting EnableAllFolders: false");

                // Get user ID as Guid
                var userIdValue = user.GetType().GetProperty("Id")?.GetValue(user);
                if (userIdValue == null)
                {
                    _exportLogger.Log($"❌ ERROR: Could not get user ID");
                    return;
                }

                Guid userGuid;
                if (userIdValue is Guid guid)
                {
                    userGuid = guid;
                }
                else if (!Guid.TryParse(userIdValue.ToString(), out userGuid))
                {
                    _exportLogger.Log($"❌ ERROR: Invalid user ID format: {userIdValue}");
                    return;
                }

                // Create new policy object
                var policyProp = user.GetType().GetProperty("Policy");
                var policy = policyProp?.GetValue(user) as UserPolicy;
                policy = new UserPolicy
                {
                    EnableAllFolders = false
                };

                await _userManager.UpdatePolicyAsync(userGuid, policy).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _exportLogger.Log($"❌ ERROR updating permissions: {ex.Message}");
                _exportLogger.LogError($"Stack trace: {ex.StackTrace}", ex);
            }
        }

        _exportLogger.Log($"");
    }

    private void TryUpdatePasswordHash(string username, string passwordHash)
    {
        try
        {
            var dbPath = FindDbPath("jellyfin.db");
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                _exportLogger.Log("Password hash present but jellyfin.db not found for update.");
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Password = @Password WHERE lower(Username) = lower(@Username)";
            cmd.Parameters.AddWithValue("@Password", passwordHash);
            cmd.Parameters.AddWithValue("@Username", username);
            var rows = cmd.ExecuteNonQuery();

            var pwMsg = rows > 0
                ? $"Updated password hash for user '{username}' in DB."
                : $"No DB row updated for user '{username}' (user may not exist yet).";
            _exportLogger.Log(pwMsg);
        }
        catch (Exception ex)
        {
            _exportLogger.Log($"Failed to update password hash for '{username}': {ex.Message}");
        }
    }

    private string? FindDbPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(_paths.DataPath, fileName),
            Path.Combine(_paths.DataPath, "data", fileName),
            Path.Combine(_paths.DataPath, "root", fileName),
            Path.Combine(Path.GetDirectoryName(_paths.DataPath) ?? string.Empty, fileName),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Handles user data export operations.
/// </summary>
public class UserExporter
{
    private readonly IApplicationPaths _paths;
    private readonly ExportLogger _exportLogger;
    private readonly ILogger _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserViewManager _userViewManager;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="UserExporter"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="exportLogger">Export logger instance.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">User manager for accessing user data.</param>
    /// <param name="libraryManager">Library manager for accessing library data.</param>
    /// <param name="userViewManager">User view manager for accessing user libraries.</param>
    public UserExporter(IApplicationPaths paths, ExportLogger exportLogger, ILogger logger, IUserManager userManager, ILibraryManager libraryManager, IUserViewManager userViewManager)
    {
        _paths = paths;
        _exportLogger = exportLogger;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userViewManager = userViewManager;
    }

    /// <summary>
    /// Exports user data to the specified output path.
    /// </summary>
    /// <param name="outputPath">The output file path.</param>
    /// <param name="filterUserIds">Optional list of user IDs to filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of users exported.</returns>
    public async Task<int> ExportUsersAsync(string outputPath, IList<string>? filterUserIds, CancellationToken cancellationToken)
    {
        _exportLogger.Log("Starting user export via Jellyfin services");

        try
        {
            // Get plugin configuration to know which libraries are selected
            var config = Jellyfin.Plugin.Template.Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();

            // Get library mapping
            var libraryMap = GetLibraryMapping();

            // Get password hashes from database
            var passwordHashes = GetPasswordHashesFromDatabase();

            // Get users
            var users = GetUsers(filterUserIds, libraryMap, passwordHashes, config);

            await WriteJsonAsync(outputPath, users, cancellationToken).ConfigureAwait(false);
            _exportLogger.Log($"Exported {users.Count} users");
            return users.Count;
        }
        catch (Exception ex)
        {
            _exportLogger.LogError($"Export failed: {ex.Message}", ex);
            throw;
        }
    }

    private Dictionary<string, string> GetLibraryMapping()
    {
        _exportLogger.Log("Fetching library information...");

        var libraryMap = new Dictionary<string, string>();
        var virtualFolders = _libraryManager.GetVirtualFolders();

        foreach (var folder in virtualFolders)
        {
            if (!string.IsNullOrEmpty(folder.ItemId) && !string.IsNullOrEmpty(folder.Name))
            {
                libraryMap[folder.ItemId] = folder.Name;
            }
        }

        _exportLogger.Log($"Found {libraryMap.Count} libraries");
        return libraryMap;
    }

    private List<object> GetUsers(IList<string>? filterUserIds, Dictionary<string, string> libraryMap, Dictionary<string, string?> passwordHashes, Configuration.PluginConfiguration config)
    {
        _exportLogger.Log("Fetching user information...");

        var allUsers = _userManager.Users;
        var users = new List<object>();

        foreach (var user in allUsers)
        {
            var id = user.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var username = user.Username;

            // Apply user filter if specified
            if (filterUserIds is { Count: > 0 })
            {
                var normalizedId = id.Replace("-", string.Empty, StringComparison.Ordinal);
                var isIncluded = filterUserIds.Any(filterId =>
                    string.Equals(filterId.Replace("-", string.Empty, StringComparison.Ordinal), normalizedId, StringComparison.OrdinalIgnoreCase));

                if (!isIncluded)
                {
                    continue;
                }
            }

            // Get user's accessible libraries using proper Jellyfin API
            var allUserLibraries = GetAccessibleLibrariesForUser(user);

            // Filter libraries based on selected libraries in configuration
            var libraries = FilterLibrariesBySelection(allUserLibraries, config);

            // Get password hash for this user
            var passwordHash = passwordHashes.TryGetValue(username, out var hash) ? hash : null;

            users.Add(new { id, username, passwordHash, libraries });
        }

        return users;
    }

    private List<object> FilterLibrariesBySelection(List<object> userLibraries, Configuration.PluginConfiguration config)
    {
        // If no specific libraries are selected, include all user libraries
        if (config.SelectedLibraryIds == null || config.SelectedLibraryIds.Count == 0)
        {
            _exportLogger.Log("No library filter specified - including all user libraries");
            return userLibraries;
        }

        var filteredLibraries = new List<object>();
        var selectedLibraryIds = config.SelectedLibraryIds;

        foreach (dynamic library in userLibraries)
        {
            var libraryId = library.id?.ToString() ?? string.Empty;

            // Check if this library is in the selected list (with GUID normalization)
            var normalizedLibraryId = libraryId.Replace("-", string.Empty, StringComparison.Ordinal);
            var isSelected = selectedLibraryIds.Any(selectedId =>
                string.Equals(selectedId.Replace("-", string.Empty, StringComparison.Ordinal), normalizedLibraryId, StringComparison.OrdinalIgnoreCase));

            if (isSelected)
            {
                filteredLibraries.Add(library);
                _exportLogger.Log($"Including library: {library.name} ({libraryId})");
            }
            else
            {
                _exportLogger.Log($"Excluding library: {library.name} ({libraryId}) - not selected in config");
            }
        }

        _exportLogger.Log($"Filtered libraries: {filteredLibraries.Count} of {userLibraries.Count} included");
        return filteredLibraries;
    }

    private Dictionary<string, string?> GetPasswordHashesFromDatabase()
    {
        var passwordHashes = new Dictionary<string, string?>();

        try
        {
            var jellyfinDb = FindDbPath("jellyfin.db");
            if (string.IsNullOrEmpty(jellyfinDb) || !File.Exists(jellyfinDb))
            {
                _exportLogger.Log("jellyfin.db not found - password hashes will not be available");
                return passwordHashes;
            }

            _exportLogger.Log($"Extracting password hashes from: {jellyfinDb}");

            using var conn = new SqliteConnection($"Data Source={jellyfinDb}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Username, Password FROM Users";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var username = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var passwordHash = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();

                if (!string.IsNullOrEmpty(username))
                {
                    passwordHashes[username] = passwordHash;
                    _exportLogger.Log($"Found password hash for user: {username}");
                }
            }

            _exportLogger.Log($"Extracted password hashes for {passwordHashes.Count} users");
        }
        catch (Exception ex)
        {
            _exportLogger.LogError($"Failed to extract password hashes: {ex.Message}", ex);
        }

        return passwordHashes;
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

    private List<object> GetAccessibleLibrariesForUser(dynamic user)
    {
        var libraries = new List<object>();

        try
        {
            // Create a UserViewQuery for this user - check what properties are actually available
            var query = new UserViewQuery();

            // Try different ways to set the user
            if (query.GetType().GetProperty("User") != null)
            {
                query.GetType().GetProperty("User")?.SetValue(query, user);
            }

            // Get all libraries/views the user can access
            var views = _userViewManager.GetUserViews(query);

            foreach (var view in views)
            {
                if (view != null && !string.IsNullOrEmpty(view.Name))
                {
                    var library = new
                    {
                        id = view.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture),
                        name = view.Name
                    };
                    libraries.Add(library);
                    _exportLogger.Log($"User {user.Username} has access to library: {view.Name} ({view.Id})");
                }
            }

            _exportLogger.Log($"User {user.Username} has access to {libraries.Count} libraries");
        }
        catch (Exception ex)
        {
            _exportLogger.LogError($"Failed to get accessible libraries for user {user.Username}: {ex.Message}");

            // Fallback: try to use the library manager directly
            var virtualFolders = _libraryManager.GetVirtualFolders();
            foreach (var folder in virtualFolders)
            {
                if (!string.IsNullOrEmpty(folder.ItemId) && !string.IsNullOrEmpty(folder.Name))
                {
                    libraries.Add(new { id = folder.ItemId, name = folder.Name });
                }
            }
        }

        return libraries;
    }

    private static async Task WriteJsonAsync(string path, object data, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, data, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}

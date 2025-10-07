using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Exports per-user watch history to separate JSON files.
/// </summary>
public class WatchHistoryExporter
{
    private readonly IApplicationPaths _paths;
    private readonly ExportLogger _exportLogger;
    private readonly ILogger _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] UserIdCandidates = { "UserId", "User_Id" };
    private static readonly string[] ItemIdCandidates = { "ItemId", "Item_Id" };
    private static readonly string[] KeyCandidates = { "Key", "ItemKey" };

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryExporter"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="exportLogger">Export logger instance.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">User manager service.</param>
    /// <param name="libraryManager">Library manager service.</param>
    /// <param name="userDataManager">User data manager service.</param>
    public WatchHistoryExporter(
        IApplicationPaths paths,
        ExportLogger exportLogger,
        ILogger logger,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        _paths = paths;
        _exportLogger = exportLogger;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    /// <summary>
    /// Export per-user watch history into the specified directory.
    /// </summary>
    /// <param name="outputDir">Directory to store per-user files.</param>
    /// <param name="filterUserIds">Optional list of user IDs to include (N-format guids or with dashes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of user files written.</returns>
    public async Task<int> ExportPerUserAsync(string outputDir, IList<string>? filterUserIds, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDir);
        _exportLogger.Log($"Starting watch-history export to: {outputDir}");
        _exportLogger.Log("Watch-history strategy: IUserDataManager + Library enumeration (no SQLite)");

        var users = _userManager.Users;
        var userIdToName = users.ToDictionary(
            u => u.Id.ToString("N", CultureInfo.InvariantCulture),
            u => u.Username,
            StringComparer.OrdinalIgnoreCase);

        HashSet<string>? filter = null;
        if (filterUserIds is { Count: > 0 })
        {
            filter = new HashSet<string>(filterUserIds.Select(id => id.Replace("-", string.Empty, StringComparison.Ordinal)), StringComparer.OrdinalIgnoreCase);
        }

        var perUser = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Enumerate all item ids using library services (no direct DB access).
            var allItemIds = await GetAllItemIdsAsync(cancellationToken).ConfigureAwait(false);

            foreach (var user in users)
            {
                var userIdN = user.Id.ToString("N", CultureInfo.InvariantCulture);
                if (filter is not null && !filter.Contains(userIdN))
                {
                    continue;
                }

                foreach (var itemId in allItemIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var item = _libraryManager.GetItemById(itemId);
                    if (item is null)
                    {
                        continue;
                    }

                    // Prefer using IUserDataManager.GetUserDataDto(BaseItem, User) when available.
                    var userData = GetUserDataFor(_userDataManager, user, item) ?? GetUserData(_userDataManager, user, itemId);
                    if (userData is null)
                    {
                        continue;
                    }

                    var played = TryGetBool(userData, "Played") ?? false;
                    var playCount = TryGetInt(userData, "PlayCount") ?? 0;
                    var isFavorite = TryGetBool(userData, "IsFavorite") ?? false;
                    var playbackPositionTicks = TryGetLong(userData, "PlaybackPositionTicks") ?? 0L;
                    var lastPlayedUtc = TryGetNullableDateTime(userData, "LastPlayedDate");

                    // Exclude records with no lastPlayedUtc, no progress, and no play count
                    // (even if other flags like Played/IsFavorite are set)
                    if (lastPlayedUtc is null && playbackPositionTicks == 0 && playCount == 0)
                    {
                        continue;
                    }

                    var identity = BuildIdentityForItem(item);

                    if (!perUser.TryGetValue(userIdN, out var list))
                    {
                        list = new List<object>();
                        perUser[userIdN] = list;
                    }

                    list.Add(new
                    {
                        ids = identity,
                        played,
                        playCount,
                        isFavorite,
                        playbackPositionTicks,
                        lastPlayedUtc
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _exportLogger.LogError($"Failed to export watch history: {ex.Message}", ex);
        }

        var fileCount = 0;
        foreach (var kvp in perUser)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userIdN = kvp.Key;
            var items = kvp.Value;
            var username = userIdToName.TryGetValue(userIdN, out var name) ? name : userIdN;
            var fileName = SanitizeFileName(username) + ".json";
            var outPath = Path.Combine(outputDir, fileName);

            using var fs = File.Create(outPath);
            await JsonSerializer.SerializeAsync(fs, items, JsonOptions, cancellationToken).ConfigureAwait(false);
            fileCount++;

            _exportLogger.Log($"Wrote watch history for user '{username}' ({items.Count} items) -> {outPath}");
        }

        _exportLogger.Log($"Watch-history export completed: {fileCount} file(s) written");
        return fileCount;
    }

    private object BuildIdentityForItem(BaseItem? item)
    {
        if (item is null)
        {
            return new { id = (string?)null };
        }

        var id = item.Id.ToString("N", CultureInfo.InvariantCulture);
        var providerIds = item.ProviderIds;
        var type = item.GetType().Name;
        var name = item.Name;
        var path = TryGetString(item, "Path");
        var productionYear = TryGetInt(item, "ProductionYear");

        var seriesName = TryGetString(item, "SeriesName");
        var seasonNumber = TryGetInt(item, "ParentIndexNumber");
        var episodeNumber = TryGetInt(item, "IndexNumber");

        return new
        {
            id,
            type,
            name,
            providerIds,
            path,
            productionYear,
            seriesName,
            seasonNumber,
            episodeNumber
        };
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

    private string? FindLibraryDbPath()
    {
        var names = new[] { "library.db", "library-v2.db", "library_v2.db", "jellyfin.db" };
        foreach (var n in names)
        {
            var path = FindDbPath(n);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Detects a compatible user-data schema and executes a normalized select reader.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reader with columns: UserId, ItemId, Played, PlayCount, IsFavorite, PlaybackPositionTicks, LastPlayedDate.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Identifiers are derived from sqlite_master; no user input is involved.")]
    private static async Task<SqliteDataReader> BuildAndExecuteWatchedQueryAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        var tableNames = new List<string>();

        var listCmd = conn.CreateCommand();
        await using (listCmd)
        {
            listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND (name='UserDatas' OR name='UserData' OR name LIKE '%User%Data%')";
            var listReader = await listCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (await listReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    tableNames.Add(listReader.GetString(0));
                }
            }
            finally
            {
                await listReader.DisposeAsync().ConfigureAwait(false);
            }
        }

        var hasItems = false;
        var itemsHasId = false;
        var itemsHasPUK = false;

        var itemsCheck = conn.CreateCommand();
        await using (itemsCheck)
        {
            itemsCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Items'";
            var itemsReader = await itemsCheck.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                hasItems = await itemsReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await itemsReader.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (hasItems)
        {
            var itemCols = await GetColumnsAsync(conn, "Items", cancellationToken).ConfigureAwait(false);
            itemsHasId = itemCols.Contains("Id", StringComparer.OrdinalIgnoreCase);
            itemsHasPUK = itemCols.Contains("PresentationUniqueKey", StringComparer.OrdinalIgnoreCase);
        }

        foreach (var table in tableNames)
        {
            var cols = await GetColumnsAsync(conn, table, cancellationToken).ConfigureAwait(false);

            var userIdCol = FirstExisting(cols, UserIdCandidates);
            var itemIdCol = FirstExisting(cols, ItemIdCandidates);
            var keyCol = FirstExisting(cols, KeyCandidates);

            if (string.IsNullOrEmpty(userIdCol))
            {
                continue;
            }

            string selectItemId;
            string joinClause = string.Empty;
            if (!string.IsNullOrEmpty(itemIdCol))
            {
                selectItemId = $"d.{itemIdCol}";
            }
            else if (!string.IsNullOrEmpty(keyCol) && hasItems)
            {
                if (itemsHasPUK)
                {
                    selectItemId = "i.Id";
                    joinClause = $" JOIN Items i ON i.PresentationUniqueKey = REPLACE(d.{keyCol}, '-', '')";
                }
                else if (itemsHasId)
                {
                    selectItemId = "i.Id";
                    joinClause = $" JOIN Items i ON i.Id = REPLACE(d.{keyCol}, '-', '')";
                }
                else
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            bool Has(string name) => cols.Contains(name, StringComparer.OrdinalIgnoreCase);
            string BoolOrZero(string name) => Has(name) ? $"d.{name}" : "0";
            string NumberOrZero(string name) => Has(name) ? $"d.{name}" : "0";
            string NullableOrNull(string name) => Has(name) ? $"d.{name}" : "NULL";

            var playedExpr = BoolOrZero("Played");
            var playCountExpr = NumberOrZero("PlayCount");
            var favExpr = BoolOrZero("IsFavorite");
            var posExpr = NumberOrZero("PlaybackPositionTicks");
            var lastExpr = NullableOrNull("LastPlayedDate");

            var sql = $"SELECT d.{userIdCol} AS UserId, {selectItemId} AS ItemId, {playedExpr} AS Played, {playCountExpr} AS PlayCount, {favExpr} AS IsFavorite, {posExpr} AS PlaybackPositionTicks, {lastExpr} AS LastPlayedDate FROM {table} d{joinClause}";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            try
            {
                var rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return (SqliteDataReader)rdr;
            }
            catch (SqliteException)
            {
                // Try next table or pattern
            }
        }

        throw new InvalidOperationException("Unable to query watch history: no compatible UserData(s) schema found.");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Identifier quoted; table name originates from sqlite_master.")]
    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection conn, string table, CancellationToken cancellationToken)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cmd = conn.CreateCommand();
        await using (cmd)
        {
            cmd.CommandText = $"PRAGMA table_info('{table.Replace("'", "''", StringComparison.Ordinal)}')";
            var rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (rdr)
            {
                while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var name = rdr.GetString(1);
                    cols.Add(name);
                }
            }
        }

        return cols;
    }

    private static string FirstExisting(HashSet<string> cols, string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (cols.Contains(c))
            {
                return c;
            }
        }

        return string.Empty;
    }

    private async Task<List<Guid>> GetAllItemIdsAsync(CancellationToken cancellationToken)
    {
        // Try several library-first strategies to enumerate all item IDs without touching SQLite.
        var result = new HashSet<Guid>();

        // Ensure this remains truly async to avoid analyzers complaining about sync async method
        await Task.Yield();

        try
        {
            // Strategy 1: ILibraryManager.GetItemIds(InternalItemsQuery { Recursive = true })
            var lm = _libraryManager;
            var lmType = lm.GetType();

            var assemblies = new List<System.Reflection.Assembly> { lmType.Assembly };
            assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());

            var internalItemsQueryType = assemblies
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return Array.Empty<Type>();
                    }
                })
                .FirstOrDefault(t => string.Equals(t.Name, "InternalItemsQuery", StringComparison.Ordinal));

            if (internalItemsQueryType is not null)
            {
                var query = Activator.CreateInstance(internalItemsQueryType);

                // Set common properties if present
                TrySetProperty(query, "Recursive", true);
                TrySetProperty(query, "IsVirtualItem", false);

                // Prefer method named GetItemIds(query)
                var getItemIds = lmType.GetMethods()
                    .FirstOrDefault(m => string.Equals(m.Name, "GetItemIds", StringComparison.Ordinal)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.Name == internalItemsQueryType.Name);

                if (getItemIds != null)
                {
                    var idsObj = getItemIds.Invoke(lm, new[] { query });
                    foreach (var g in EnumerateGuids(idsObj))
                    {
                        result.Add(g);
                    }
                }
                else
                {
                    // Fallback: GetItems(query) -> result.Items -> BaseItem.Id
                    var getItems = lmType.GetMethods()
                        .FirstOrDefault(m => string.Equals(m.Name, "GetItems", StringComparison.Ordinal)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType.Name == internalItemsQueryType.Name);

                    if (getItems != null)
                    {
                        var itemsResult = getItems.Invoke(lm, new[] { query });
                        var itemsEnumerable = TryGetProperty(itemsResult, "Items") as System.Collections.IEnumerable;
                        if (itemsEnumerable != null)
                        {
                            foreach (var item in itemsEnumerable)
                            {
                                var idObj = TryGetProperty(item!, "Id");
                                if (idObj is Guid g)
                                {
                                    result.Add(g);
                                }
                            }
                        }
                    }
                }
            }

            // Strategy 2: Traverse from virtual folders via RootFolder and recursive children
            if (result.Count == 0)
            {
                var folders = _libraryManager.GetVirtualFolders();
                foreach (var vf in folders)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var itemIdStr = (string?)TryGetProperty(vf, "ItemId") ?? string.Empty;
                    if (!Guid.TryParse(itemIdStr, out var folderId))
                    {
                        continue;
                    }

                    var rootItem = _libraryManager.GetItemById(folderId);
                    if (rootItem is null)
                    {
                        continue;
                    }

                    foreach (var g in EnumerateItemIdsRecursive(rootItem, _userManager.Users.FirstOrDefault()))
                    {
                        result.Add(g);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _exportLogger.Log($"Item enumeration via library failed: {ex.Message}");
        }

        _exportLogger.Log($"Discovered {result.Count} item ids for user-data scan");
        return result.ToList();
    }

    private static IEnumerable<Guid> EnumerateGuids(object? idsObj)
    {
        if (idsObj is null)
        {
            yield break;
        }

        if (idsObj is System.Collections.IEnumerable en)
        {
            foreach (var o in en)
            {
                if (o is Guid g)
                {
                    yield return g;
                }
                else if (o is string s && Guid.TryParse(s, out var gs))
                {
                    yield return gs;
                }
            }
        }
    }

    private IEnumerable<Guid> EnumerateItemIdsRecursive(object rootItem, object? user)
    {
        var seen = new HashSet<Guid>();

        // First try: a single call to GetRecursiveChildren(user?)
        var children = InvokeChildren(rootItem, user, recursive: true) ?? InvokeChildren(rootItem, user, recursive: false);
        if (children is not null)
        {
            foreach (var it in children)
            {
                var idObj = TryGetProperty(it!, "Id");
                if (idObj is Guid g && seen.Add(g))
                {
                    yield return g;
                }
            }

            yield break;
        }

        // Fallback: BFS using GetChildren
        var stack = new Stack<object>();
        stack.Push(rootItem);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            var idObj = TryGetProperty(cur, "Id");
            if (idObj is Guid gid && seen.Add(gid))
            {
                yield return gid;
            }

            var directChildren = InvokeChildren(cur, user, recursive: false);
            if (directChildren is null)
            {
                continue;
            }

            foreach (var ch in directChildren)
            {
                if (ch is not null)
                {
                    stack.Push(ch);
                }
            }
        }
    }

    private static System.Collections.IEnumerable? InvokeChildren(object item, object? user, bool recursive)
    {
        var methods = item.GetType().GetMethods();
        var preferredName = recursive ? "GetRecursiveChildren" : "GetChildren";

        foreach (var m in methods)
        {
            if (!string.Equals(m.Name, preferredName, StringComparison.Ordinal))
            {
                continue;
            }

            var ps = m.GetParameters();
            try
            {
                if (ps.Length == 0)
                {
                    var res = m.Invoke(item, Array.Empty<object?>());
                    if (res is System.Collections.IEnumerable en)
                    {
                        return en;
                    }
                }
                else if (ps.Length == 1)
                {
                    // Likely an overload that takes a User. Try to pass a user if we have one; otherwise null.
                    var res = m.Invoke(item, new[] { user });
                    if (res is System.Collections.IEnumerable en)
                    {
                        return en;
                    }
                }
            }
            catch
            {
                // Try next overload
            }
        }

        // Try property-based access as a last resort
        var propName = recursive ? "RecursiveChildren" : "Children";
        var prop = item.GetType().GetProperty(propName);
        var val = prop?.GetValue(item);
        return val as System.Collections.IEnumerable;
    }

    private static void TrySetProperty(object? obj, string propertyName, object? value)
    {
        if (obj is null)
        {
            return;
        }

        try
        {
            var p = obj.GetType().GetProperty(propertyName);
            p?.SetValue(obj, value);
        }
        catch
        {
            // ignore
        }
    }

    private static object? TryGetProperty(object? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static object? GetUserDataFor(IUserDataManager manager, object user, BaseItem item)
    {
        try
        {
            var methods = manager.GetType().GetMethods().Where(m =>
                string.Equals(m.Name, "GetUserDataDto", StringComparison.Ordinal) ||
                string.Equals(m.Name, "GetUserData", StringComparison.Ordinal)).ToList();
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length != 2)
                {
                    continue;
                }

                try
                {
                    // Common modern signature: GetUserDataDto(BaseItem, User)
                    if (ps[0].ParameterType.IsAssignableFrom(item.GetType()) && ps[1].ParameterType.IsAssignableFrom(user.GetType()))
                    {
                        return m.Invoke(manager, new object[] { item, user });
                    }

                    // Possible legacy signature: GetUserData(Guid userId, Guid itemId)
                    var userIdProp = user.GetType().GetProperty("Id");
                    var userId = userIdProp?.GetValue(user);
                    var itemId = TryGetProperty(item, "Id");
                    if (userId is Guid guid && itemId is Guid ig && ps[0].ParameterType == typeof(Guid) && ps[1].ParameterType == typeof(Guid))
                    {
                        return m.Invoke(manager, new object[] { guid, ig });
                    }
                }
                catch
                {
                    // Try next overload
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static object? GetUserData(IUserDataManager manager, object user, Guid itemId)
    {
        // Legacy fallback kept for backward compatibility when only (Guid, Guid) overload exists.
        try
        {
            var methods = manager.GetType().GetMethods().Where(m => string.Equals(m.Name, "GetUserData", StringComparison.Ordinal)).ToList();
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length != 2)
                {
                    continue;
                }

                try
                {
                    if (ps[0].ParameterType.IsAssignableFrom(user.GetType()) && ps[1].ParameterType == typeof(Guid))
                    {
                        return m.Invoke(manager, new object[] { user, itemId });
                    }

                    var userIdProp = user.GetType().GetProperty("Id");
                    var userId = userIdProp?.GetValue(user);
                    if (userId is Guid guid && ps[0].ParameterType == typeof(Guid) && ps[1].ParameterType == typeof(Guid))
                    {
                        return m.Invoke(manager, new object[] { guid, itemId });
                    }
                }
                catch
                {
                    // Try next overload
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool? TryGetBool(object obj, string propertyName)
    {
        var p = obj.GetType().GetProperty(propertyName);
        var v = p?.GetValue(obj);
        if (v is null)
        {
            return null;
        }

        if (v is bool b)
        {
            return b;
        }

        try
        {
            return Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetInt(object obj, string propertyName)
    {
        var p = obj.GetType().GetProperty(propertyName);
        var v = p?.GetValue(obj);
        if (v is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static long? TryGetLong(object obj, string propertyName)
    {
        var p = obj.GetType().GetProperty(propertyName);
        var v = p?.GetValue(obj);
        if (v is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? TryGetNullableDateTime(object obj, string propertyName)
    {
        var p = obj.GetType().GetProperty(propertyName);
        var v = p?.GetValue(obj);
        if (v is null)
        {
            return null;
        }

        if (v is DateTime dt)
        {
            return dt;
        }

        try
        {
            return DateTime.Parse(v.ToString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "user" : sanitized;
    }

    private static string? TryGetString(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        var val = prop?.GetValue(obj);
        return val?.ToString();
    }

    // Note: There is an earlier TryGetInt overload for generic object userData; keep only one.
}

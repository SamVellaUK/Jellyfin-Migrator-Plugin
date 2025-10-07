using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Exports registered devices, optionally filtered by selected users.
/// </summary>
public class DeviceExporter
{
    private readonly ExportLogger _exportLogger;
    private readonly MediaBrowser.Common.Configuration.IApplicationPaths _paths;
    private readonly IDeviceManager _deviceManager;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceExporter"/> class.
    /// </summary>
    /// <param name="exportLogger">Export logger.</param>
    /// <param name="deviceManager">Device manager service.</param>
    /// <param name="userManager">User manager service.</param>
    /// <param name="sessionManager">Session manager service.</param>
    /// <param name="paths">Application paths.</param>
    public DeviceExporter(ExportLogger exportLogger, IDeviceManager deviceManager, IUserManager userManager, ISessionManager sessionManager, MediaBrowser.Common.Configuration.IApplicationPaths paths)
    {
        _exportLogger = exportLogger;
        _deviceManager = deviceManager;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _paths = paths;
    }

    /// <summary>
    /// Export devices to the given file.
    /// </summary>
    /// <param name="outputPath">JSON output path.</param>
    /// <param name="filterUserIds">Optional list of user ids (N-format or dashed) to include devices for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Count of exported devices.</returns>
    public async Task<int> ExportAsync(string outputPath, IList<string>? filterUserIds, CancellationToken cancellationToken)
    {
        _exportLogger.Log("Starting device export via IDeviceManager");

        // Build filters
        HashSet<Guid>? userFilter = null;
        if (filterUserIds is { Count: > 0 })
        {
            userFilter = new HashSet<Guid>();
            foreach (var id in filterUserIds)
            {
                var norm = id.Replace("-", string.Empty, StringComparison.Ordinal);
                if (Guid.TryParseExact(norm, "N", out var g))
                {
                    userFilter.Add(g);
                }
                else if (Guid.TryParse(id, out g))
                {
                    userFilter.Add(g);
                }
            }
        }

        var result = new List<DeviceExport>();

        // Get devices using reflection to avoid hard dependency on specific Jellyfin versions
        System.Collections.IEnumerable devices = GetDevicesEnumerable();

        foreach (var d in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Properties from MediaBrowser.Model.Devices.DeviceInfo
            var id = TryGetString(d, "Id") ?? string.Empty;
            var lastUserIdObj = TryGetProperty(d, "LastUserId");
            Guid? lastUserId = lastUserIdObj is Guid g ? g : (Guid?)null;
            var lastUserName = TryGetString(d, "LastUserName");
            var appName = TryGetString(d, "AppName");
            var appVersion = TryGetString(d, "AppVersion");
            var dateLastActivity = TryGetNullableDateTime(TryGetProperty(d, "DateLastActivity"));
            var capabilities = TryGetProperty(d, "Capabilities");

            // Apply user filter if required
            if (userFilter is not null)
            {
                if (!lastUserId.HasValue || !userFilter.Contains(lastUserId.Value))
                {
                    continue;
                }
            }

            // Optionally look up current known username by id if LastUserName is missing
            if (string.IsNullOrEmpty(lastUserName) && lastUserId.HasValue)
            {
                try
                {
                    var u = _userManager.GetUserById(lastUserId.Value);
                    lastUserName = u?.Username ?? lastUserName;
                }
                catch
                {
                    // ignore
                }
            }

            result.Add(new DeviceExport
            {
                Id = id,
                LastUserId = lastUserId?.ToString("N", CultureInfo.InvariantCulture),
                LastUserIdDashed = lastUserId?.ToString("D", CultureInfo.InvariantCulture),
                LastUserName = lastUserName,
                AppName = appName,
                AppVersion = appVersion,
                DateLastActivity = dateLastActivity,
                Capabilities = SerializeToElement(capabilities)
            });
        }

        // Enrich with authentication/session bindings via ISessionManager (service-first approach)
        var tokensIncluded = EnrichWithSessions(result, userFilter);

        // If sessions did not yield tokens, fall back to database for tokens and bindings
        if (!tokensIncluded)
        {
            tokensIncluded = EnrichWithDbTokens(result, userFilter);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Wrap in an envelope to include tokensIncluded metadata while keeping devices list
        var envelope = new DevicesEnvelope
        {
            TokensIncluded = tokensIncluded,
            Devices = result,
        };

        using var fs = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(fs, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
        _exportLogger.Log($"Exported {result.Count} device(s) (tokensIncluded={tokensIncluded}) -> {outputPath}");

        return result.Count;
    }

    private System.Collections.IEnumerable GetDevicesEnumerable()
    {
        try
        {
            var type = _deviceManager.GetType();
            var methods = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var candidates = methods.Where(m => string.Equals(m.Name, "GetDevices", StringComparison.Ordinal)
                                             || string.Equals(m.Name, "GetDevicesForUser", StringComparison.Ordinal)
                                             || string.Equals(m.Name, "GetRegisteredDevices", StringComparison.Ordinal)
                                             || string.Equals(m.Name, "GetDeviceList", StringComparison.Ordinal))
                                    .ToList();

            foreach (var m in candidates)
            {
                try
                {
                    var ps = m.GetParameters();
                    object?[] args;
                    if (ps.Length == 0)
                    {
                        args = Array.Empty<object?>();
                    }
                    else
                    {
                        args = new object?[ps.Length];
                        for (var i = 0; i < ps.Length; i++)
                        {
                            var p = ps[i];
                            if (p.HasDefaultValue)
                            {
                                args[i] = p.DefaultValue;
                            }
                            else if (p.ParameterType.IsValueType)
                            {
                                args[i] = Activator.CreateInstance(p.ParameterType);
                            }
                            else
                            {
                                args[i] = null;
                            }
                        }
                    }

                    var res = m.Invoke(_deviceManager, args);
                    if (res is System.Collections.IEnumerable en)
                    {
                        return en;
                    }

                    var itemsProp = res?.GetType().GetProperty("Items");
                    var items = itemsProp?.GetValue(res) as System.Collections.IEnumerable;
                    if (items is not null)
                    {
                        return items;
                    }
                }
                catch
                {
                    // Try next candidate
                }
            }
        }
        catch
        {
        }

        // Final fallback: derive devices from sessions (active only)
        try
        {
            var list = new List<object>();
            var sessions = EnlistSessions();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sessions)
            {
                var deviceId = TryGetString(s, "DeviceId");
                if (string.IsNullOrWhiteSpace(deviceId) || !seen.Add(deviceId!))
                {
                    continue;
                }

                list.Add(new
                {
                    Id = deviceId,
                    AppName = TryGetString(s, "Client"),
                    AppVersion = TryGetString(s, "ApplicationVersion"),
                });
            }

            return list;
        }
        catch
        {
        }

        return Array.Empty<object>();
    }

    private System.Collections.IEnumerable EnlistSessions()
    {
        System.Collections.IEnumerable sessions = Array.Empty<object>();
        try
        {
            var sessObj = (object?)_sessionManager;
            object? list = null;
            try
            {
                list = TryGetProperty(sessObj, "Sessions");
            }
            catch
            {
            }

            if (list is System.Collections.IEnumerable en)
            {
                sessions = en;
            }
            else
            {
                var getSessions = _sessionManager.GetType().GetMethod(
                    "GetSessions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (getSessions is not null)
                {
                    var res = getSessions.Invoke(_sessionManager, null);
                    if (res is System.Collections.IEnumerable en2)
                    {
                        sessions = en2;
                    }
                    else
                    {
                        var itemsProp = res?.GetType().GetProperty("Items");
                        var items = itemsProp?.GetValue(res) as System.Collections.IEnumerable;
                        sessions = items ?? Array.Empty<object>();
                    }
                }
            }
        }
        catch
        {
            sessions = Array.Empty<object>();
        }

        return sessions;
    }

    private bool EnrichWithDbTokens(List<DeviceExport> devices, HashSet<Guid>? userFilter)
    {
        try
        {
            var dbPath = FindDbPath("jellyfin.db");
            if (string.IsNullOrEmpty(dbPath))
            {
                _exportLogger.Log("jellyfin.db not found - device tokens will not be available");
                return false;
            }

            _exportLogger.Log($"Extracting device tokens from: {dbPath}");

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT d.DeviceId, d.AccessToken, d.AppName, d.AppVersion, d.DeviceName, d.IsActive,
                                        d.DateCreated, d.DateModified, d.DateLastActivity,
                                        u.Id as UserId, u.Username as UserName
                                 FROM Devices d
                                 LEFT JOIN Users u ON d.UserId = u.Id";

            using var reader = cmd.ExecuteReader();

            var byId = new Dictionary<string, DeviceExport>(StringComparer.OrdinalIgnoreCase);
            foreach (var dev in devices)
            {
                if (!string.IsNullOrEmpty(dev.Id))
                {
                    byId[dev.Id!] = dev;
                }
            }

            var any = false;
            while (reader.Read())
            {
                var deviceId = reader["DeviceId"]?.ToString();
                var token = reader["AccessToken"]?.ToString();
                var appName = reader["AppName"]?.ToString();
                var appVersion = reader["AppVersion"]?.ToString();
                var userIdStr = reader["UserId"]?.ToString();
                var userName = reader["UserName"]?.ToString();
                DateTime? lastActivity = TryReadNullableDateTime(reader, "DateLastActivity");

                Guid? userId = null;
                if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var g))
                {
                    userId = g;
                }

                if (userFilter is not null)
                {
                    if (!userId.HasValue || !userFilter.Contains(userId.Value))
                    {
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                if (!byId.TryGetValue(deviceId!, out var dev))
                {
                    dev = new DeviceExport
                    {
                        Id = deviceId,
                        AppName = appName,
                        AppVersion = appVersion,
                        DateLastActivity = lastActivity,
                        LastUserId = userId?.ToString("N", CultureInfo.InvariantCulture),
                        LastUserIdDashed = userId?.ToString("D", CultureInfo.InvariantCulture),
                        LastUserName = userName,
                    };
                    byId[deviceId] = dev;
                    devices.Add(dev);
                }

                dev.Auth ??= new List<DeviceAuth>();
                dev.Auth.Add(new DeviceAuth
                {
                    UserId = userId?.ToString("N", CultureInfo.InvariantCulture),
                    UserIdDashed = userId?.ToString("D", CultureInfo.InvariantCulture),
                    UserName = userName,
                    Token = token,
                    Client = appName,
                    LastActivity = lastActivity,
                });

                if (!string.IsNullOrEmpty(token))
                {
                    any = true;
                }
            }

            _exportLogger.Log($"Extracted device tokens for {devices.Count} device entries");
            return any;
        }
        catch (Exception ex)
        {
            _exportLogger.LogError($"Failed to extract device tokens via DB: {ex.Message}", ex);
            return false;
        }
    }

    private string? FindDbPath(string fileName)
    {
        try
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(_paths.DataPath, fileName),
                System.IO.Path.Combine(_paths.DataPath, "data", fileName),
                System.IO.Path.Combine(_paths.DataPath, "root", fileName),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_paths.DataPath) ?? string.Empty, fileName),
            };

            foreach (var p in candidates)
            {
                if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                {
                    return p;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static DateTime? TryReadNullableDateTime(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            var v = reader.GetValue(ordinal);
            if (v is DateTime dt)
            {
                return dt;
            }

            var s = v.ToString();
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
        }

        return null;
    }

    private static object? TryGetProperty(object? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            var p = obj.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            return p?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetString(object? obj, string propertyName)
    {
        var v = TryGetProperty(obj, propertyName);
        return v?.ToString();
    }

    private static DateTime? TryGetNullableDateTime(object? obj)
    {
        if (obj is null)
        {
            return null;
        }

        if (obj is DateTime dt)
        {
            return dt;
        }

        var s = obj.ToString();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private bool EnrichWithSessions(List<DeviceExport> devices, HashSet<Guid>? userFilter)
    {
        var anyTokens = false;

        // Map device id -> dto for quick lookup
        var byId = new Dictionary<string, DeviceExport>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices)
        {
            if (!string.IsNullOrEmpty(d.Id) && !byId.ContainsKey(d.Id!))
            {
                byId[d.Id!] = d;
            }
        }

        System.Collections.IEnumerable sessions = Array.Empty<object>();
        try
        {
            // Attempt common patterns across versions
            var sessObj = (object?)_sessionManager;

            object? list = null;
            try
            {
                list = TryGetProperty(sessObj, "Sessions");
            }
            catch
            {
            }

            if (list is System.Collections.IEnumerable en)
            {
                sessions = en;
            }
            else
            {
                var getSessions = _sessionManager.GetType().GetMethod(
                    "GetSessions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (getSessions is not null)
                {
                    var res = getSessions.Invoke(_sessionManager, null);
                    if (res is System.Collections.IEnumerable en2)
                    {
                        sessions = en2;
                    }
                    else
                    {
                        var itemsProp = res?.GetType().GetProperty("Items");
                        var items = itemsProp?.GetValue(res) as System.Collections.IEnumerable;
                        sessions = items ?? Array.Empty<object>();
                    }
                }
            }
        }
        catch
        {
            // No sessions available
            sessions = Array.Empty<object>();
        }

        foreach (var s in sessions)
        {
            try
            {
                var deviceId = TryGetString(s, "DeviceId");
                var client = TryGetString(s, "Client");
                var userIdObj = TryGetProperty(s, "UserId");
                Guid? userId = userIdObj is Guid g ? g : (Guid?)null;
                var userName = TryGetString(s, "UserName");
                var lastAct = TryGetNullableDateTime(TryGetProperty(s, "LastActivityDate"))
                              ?? TryGetNullableDateTime(TryGetProperty(s, "LastActivity"));

                // Token can appear under different names
                var token = TryGetString(s, "AccessToken")
                            ?? TryGetString(s, "Token")
                            ?? TryGetString(s, "AuthorizationToken");

                if (userFilter is not null)
                {
                    if (!userId.HasValue || !userFilter.Contains(userId.Value))
                    {
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                if (!byId.TryGetValue(deviceId!, out var dev))
                {
                    // If session has deviceId not in device list, create a placeholder entry
                    dev = new DeviceExport { Id = deviceId };
                    byId[deviceId] = dev;
                    devices.Add(dev);
                }

                dev.Auth ??= new List<DeviceAuth>();
                dev.Auth.Add(new DeviceAuth
                {
                    UserId = userId?.ToString("N", CultureInfo.InvariantCulture),
                    UserIdDashed = userId?.ToString("D", CultureInfo.InvariantCulture),
                    UserName = userName,
                    Token = token,
                    Client = client,
                    LastActivity = lastAct,
                });

                if (!string.IsNullOrEmpty(token))
                {
                    anyTokens = true;
                }
            }
            catch
            {
                // Ignore malformed session entry
            }
        }

        return anyTokens;
    }

    private static JsonElement? SerializeToElement(object? obj)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(obj, JsonOptions);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private sealed class DevicesEnvelope
    {
        public bool TokensIncluded { get; set; }

        public List<DeviceExport> Devices { get; set; } = new List<DeviceExport>();
    }

    private sealed class DeviceExport
    {
        public string? Id { get; set; }

        public string? LastUserId { get; set; }

        public string? LastUserIdDashed { get; set; }

        public string? LastUserName { get; set; }

        public string? AppName { get; set; }

        public string? AppVersion { get; set; }

        public DateTime? DateLastActivity { get; set; }

        public JsonElement? Capabilities { get; set; }

        public List<DeviceAuth>? Auth { get; set; }
    }

    private sealed class DeviceAuth
    {
        public string? UserId { get; set; }

        public string? UserIdDashed { get; set; }

        public string? UserName { get; set; }

        public string? Token { get; set; }

        public string? Client { get; set; }

        public DateTime? LastActivity { get; set; }
    }
}

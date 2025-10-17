using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Simplified export service that coordinates user export operations.
/// </summary>
public class ExportService
{
    private readonly IApplicationPaths _paths;
    private readonly ILogger<ExportService> _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserViewManager _userViewManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IDeviceManager _deviceManager;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskManager _taskManager;
    private static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptionsCached = new()
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportService"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">User manager for accessing user data.</param>
    /// <param name="libraryManager">Library manager for accessing library data.</param>
    /// <param name="userViewManager">User view manager for accessing user libraries.</param>
    /// <param name="userDataManager">User data manager for retrieving per-user item data.</param>
    /// <param name="deviceManager">Device manager for enumerating registered devices.</param>
    /// <param name="sessionManager">Session manager for enumerating device-user auth bindings.</param>
    /// <param name="taskManager">Task manager for monitoring scheduled tasks.</param>
    public ExportService(IApplicationPaths paths, ILogger<ExportService> logger, IUserManager userManager, ILibraryManager libraryManager, IUserViewManager userViewManager, IUserDataManager userDataManager, IDeviceManager deviceManager, ISessionManager sessionManager, ITaskManager taskManager)
    {
        _paths = paths;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userViewManager = userViewManager;
        _userDataManager = userDataManager;
        _deviceManager = deviceManager;
        _sessionManager = sessionManager;
        _taskManager = taskManager;
    }

    /// <summary>
    /// Runs the simplified export workflow.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the export operation.</returns>
    public async Task RunAsync(PluginConfiguration config, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(0);

        var exportRoot = GetExportDirectory();
        Directory.CreateDirectory(exportRoot);

        var exportLogger = new ExportLogger(_logger);
        var userExporter = new UserExporter(_paths, exportLogger, _logger, _userManager, _libraryManager, _userViewManager);
        var libraryExporter = new LibraryDefinitionsExporter(_paths, exportLogger, _libraryManager);
        var deviceExporter = new DeviceExporter(exportLogger, _deviceManager, _userManager, _sessionManager, _paths);
        var watchExporter = new WatchHistoryExporter(_paths, exportLogger, _logger, _userManager, _libraryManager, _userDataManager);

        var mode = (config.Mode ?? "Export").Trim();
        if (!string.Equals(mode, "Export", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "Verify", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "Import", StringComparison.OrdinalIgnoreCase))
        {
            mode = "Export";
        }

        exportLogger.Log($"Migrator {mode} started");
        var asm = typeof(ExportService).Assembly;
        var ver = asm.GetName().Version;
        var buildUtc = File.GetLastWriteTimeUtc(asm.Location);
        exportLogger.Log($"Plugin build: v{ver} built {buildUtc:u}");
        exportLogger.Log($"Data path: {_paths.DataPath}");
        exportLogger.Log($"Export directory: {exportRoot}");

        var selectedIds = config.SelectedUserIds ?? new List<string>();
        var selectedLibIds = config.SelectedLibraryIds ?? new List<string>();
        exportLogger.Log($"Selected user IDs filter: [{string.Join(", ", selectedIds)}] ({(selectedIds.Count == 0 ? "ALL USERS" : $"{selectedIds.Count} specific users")})");
        exportLogger.Log($"Selected library IDs filter: [{string.Join(", ", selectedLibIds)}] ({(selectedLibIds.Count == 0 ? "ALL LIBRARIES" : $"{selectedLibIds.Count} specific libraries")})");

        try
        {
            if (string.Equals(mode, "Verify", StringComparison.OrdinalIgnoreCase))
            {
                await VerifyAsync(config, exportRoot, exportLogger, selectedIds, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(mode, "Import", StringComparison.OrdinalIgnoreCase))
            {
                exportLogger.Log("Import mode selected.");
                await RunImportAsync(config, exportLogger, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var outputPath = Path.Combine(exportRoot, "users_basic.json");

                if (config.IncludeLibraries)
                {
                    var librariesPath = Path.Combine(exportRoot, "libraries.json");
                    var libCount = await libraryExporter.ExportAsync(librariesPath, cancellationToken).ConfigureAwait(false);
                    exportLogger.Log($"Library definitions export completed. {libCount} libraries exported.");
                }

                var exportedCount = await userExporter.ExportUsersAsync(outputPath, config, cancellationToken).ConfigureAwait(false);
                exportLogger.Log($"User export completed. {exportedCount} users exported.");

                if (config.IncludeWatchHistory)
                {
                    var watchDir = Path.Combine(exportRoot, "watch_history");
                    var files = await watchExporter.ExportPerUserAsync(watchDir, selectedIds, cancellationToken).ConfigureAwait(false);
                    exportLogger.Log($"Watch-history export completed. {files} file(s) written.");
                }
                else
                {
                    exportLogger.Log("IncludeWatchHistory=false; skipping watch-history export");
                }

                if (config.IncludeDevices)
                {
                    var devicesPath = Path.Combine(exportRoot, "devices.json");
                    var count = await deviceExporter.ExportAsync(devicesPath, selectedIds, cancellationToken).ConfigureAwait(false);
                    exportLogger.Log($"Device export completed. {count} device(s) written.");
                }
                else
                {
                    exportLogger.Log("IncludeDevices=false; skipping devices export");
                }
            }
        }
        catch (Exception ex)
        {
            exportLogger.LogError($"{mode} failed: {ex.Message}", ex);
        }

        progress?.Report(100);

        // Save logs
        await exportLogger.SaveLogToFileAsync(exportRoot).ConfigureAwait(false);
        exportLogger.SaveToConfiguration(exportRoot);

        // Build a ZIP of the export for client download and store as base64 in configuration
        TryStoreZipInConfiguration(exportRoot, exportLogger);
    }

    private async Task RunImportAsync(PluginConfiguration config, ExportLogger exportLogger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.LastImportZipBase64))
        {
            exportLogger.LogError("No import ZIP uploaded. Please upload an export ZIP from the Import tab.");
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(config.LastImportZipBase64);
            using var ms = new MemoryStream(bytes, writable: false);

            var importLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Import.ImportService>.Instance;
            var svc = new Import.ImportService(_paths, importLogger, _libraryManager, _taskManager);
            var result = await svc.ProcessZipAsync(ms, cancellationToken).ConfigureAwait(false);

            // Persist analysis in configuration for the dashboard to read
            var payload = System.Text.Json.JsonSerializer.Serialize(result, JsonSerializerOptionsCached);
            var cfg = Plugin.Instance?.Configuration;
            if (cfg is not null)
            {
                cfg.LastImportAnalysisJson = payload;
                cfg.LastImportExtractPath = result.ExtractedPath;
                cfg.LastImportUtc = DateTime.UtcNow;
                Plugin.Instance?.SaveConfiguration();
            }

            exportLogger.Log("Import analysis finished. Results saved to configuration.");

            // Check if we should proceed with actual import
            if (config.ImportIncludeLibraries && !string.IsNullOrWhiteSpace(config.ImportLibraryPathMappingsJson))
            {
                exportLogger.Log("=== Proceeding with Library Import ===");
                await ExecuteLibraryImportAsync(config, result, exportLogger, svc, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                exportLogger.Log("Library import not configured. Analysis only completed.");
            }

            // Save import log
            exportLogger.SaveToConfigurationAsImportLog(result.ExtractedPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            exportLogger.LogError($"Import processing failed: {ex.Message}", ex);
            exportLogger.SaveToConfigurationAsImportLog(string.Empty);
        }
    }

    private async Task ExecuteLibraryImportAsync(
        PluginConfiguration config,
        Import.ImportService.ImportAnalysisResult analysisResult,
        ExportLogger exportLogger,
        Import.ImportService svc,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse path mappings from JSON
            Dictionary<string, List<string>>? pathMappings = null;
            if (!string.IsNullOrWhiteSpace(config.ImportLibraryPathMappingsJson))
            {
                pathMappings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                    config.ImportLibraryPathMappingsJson);
            }

            if (pathMappings == null || pathMappings.Count == 0)
            {
                exportLogger.LogError("No path mappings provided for library import");
                return;
            }

            // Filter selected libraries
            var selectedLibIds = config.ImportSelectedLibraryIds ?? new List<string>();
            var librariesToImport = analysisResult.Libraries
                .Where(lib => selectedLibIds.Contains(lib.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (librariesToImport.Count == 0)
            {
                exportLogger.LogError("No libraries selected for import");
                return;
            }

            exportLogger.Log($"Libraries selected for import: {librariesToImport.Count}");
            foreach (var lib in librariesToImport)
            {
                exportLogger.Log($"  - {lib.Name} (ID: {lib.Id})");
            }

            // Execute import
            if (string.IsNullOrWhiteSpace(analysisResult.ExtractedPath))
            {
                exportLogger.LogError("Extracted path is missing");
                return;
            }

            var importedCount = await svc.ExecuteImportAsync(
                analysisResult.ExtractedPath,
                librariesToImport,
                pathMappings,
                exportLogger,
                cancellationToken).ConfigureAwait(false);

            exportLogger.Log($"=== Import Complete: {importedCount}/{librariesToImport.Count} libraries imported ===");

            // After libraries are rebuilt, optionally import users
            if ((Plugin.Instance?.Configuration?.ImportIncludeUsers ?? false) && analysisResult.Users.Count > 0)
            {
                exportLogger.Log("Proceeding with user import (after libraries rebuilt)...");
                try
                {
                    var userImporter = new Import.UserImporter(_paths, exportLogger, _logger, _userManager, _libraryManager);
                    var created = await userImporter.ImportUsersAsync(
                        analysisResult.ExtractedPath ?? string.Empty,
                        analysisResult.Users,
                        cancellationToken).ConfigureAwait(false);
                    exportLogger.Log($"User import completed: {created}/{analysisResult.Users.Count} created (existing users skipped)");
                }
                catch (Exception ex)
                {
                    exportLogger.LogError($"User import failed: {ex.Message}", ex);
                }
            }
            else
            {
                exportLogger.Log("User import disabled or no users present in analysis. Skipping users.");
            }
        }
        catch (Exception ex)
        {
            exportLogger.LogError($"Library import execution failed: {ex.Message}", ex);
        }
    }

    private string GetExportDirectory()
    {
        return Path.Combine(_paths.DataPath, "plugins", "Migrator", "exports", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    }

    private void TryStoreZipInConfiguration(string exportRoot, ExportLogger exportLogger)
    {
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                void AddFile(string filePath, string entryName)
                {
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fs = File.OpenRead(filePath);
                    fs.CopyTo(entryStream);
                }

                var baseLen = exportRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? exportRoot.Length : exportRoot.Length + 1;
                foreach (var file in Directory.EnumerateFiles(exportRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(baseLen).Replace('\\', '/');
                    AddFile(file, rel);
                }
            }

            var cfg = Plugin.Instance?.Configuration;
            if (cfg is not null)
            {
                cfg.LastExportZipBase64 = Convert.ToBase64String(ms.ToArray());
                Plugin.Instance?.SaveConfiguration();
            }
        }
        catch (Exception ex)
        {
            exportLogger.LogError($"Failed to create export ZIP: {ex.Message}", ex);
        }
    }

    private static async Task VerifyAsync(PluginConfiguration config, string exportRoot, ExportLogger exportLogger, IList<string> selectedIds, CancellationToken cancellationToken)
    {
        exportLogger.Log("Starting verify mode");

        var root = !string.IsNullOrWhiteSpace(config.VerifyDirectory)
            ? config.VerifyDirectory!
            : (!string.IsNullOrWhiteSpace(config.ExportDirectory) ? config.ExportDirectory! : (config.LastExportPath ?? exportRoot));

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            exportLogger.LogError("Verify: target directory not found. Set Export Directory to an existing export folder.");
            return;
        }

        exportLogger.Log($"Verifying export at: {root}");

        var usersPath = Path.Combine(root, "users_basic.json");
        var libsPath = Path.Combine(root, "libraries.json");
        var devicesPath = Path.Combine(root, "devices.json");
        var watchDir = Path.Combine(root, "watch_history");

        var ok = true;

        // Users
        if (!File.Exists(usersPath))
        {
            ok = false;
            exportLogger.LogError("Missing users_basic.json");
        }
        else
        {
            exportLogger.Log("Found users_basic.json");
        }

        // Libraries
        if (!File.Exists(libsPath))
        {
            ok = false;
            exportLogger.LogError("Missing libraries.json");
        }
        else
        {
            try
            {
                using var fs = File.OpenRead(libsPath);
                using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: cancellationToken).ConfigureAwait(false);
                var arr = doc.RootElement;
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                {
                    ok = false;
                    exportLogger.LogError("libraries.json is empty or invalid");
                }
                else
                {
                    exportLogger.Log($"Found {arr.GetArrayLength()} libraries");
                }
            }
            catch (Exception ex)
            {
                ok = false;
                exportLogger.LogError($"Failed to parse libraries.json: {ex.Message}", ex);
            }
        }

        // Devices
        if (!File.Exists(devicesPath))
        {
            ok = false;
            exportLogger.LogError("Missing devices.json");
        }
        else
        {
            try
            {
                using var fs = File.OpenRead(devicesPath);
                using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: cancellationToken).ConfigureAwait(false);
                var rootEl = doc.RootElement;
                var tokensIncluded = rootEl.TryGetProperty("TokensIncluded", out var t) && t.ValueKind == JsonValueKind.True;
                var devicesEl = rootEl.TryGetProperty("Devices", out var d) ? d : default;
                var count = devicesEl.ValueKind == JsonValueKind.Array ? devicesEl.GetArrayLength() : 0;
                exportLogger.Log($"Devices: {count} (tokensIncluded={tokensIncluded})");
                if (count == 0)
                {
                    ok = false;
                    exportLogger.LogError("No devices found in devices.json");
                }

                if (!tokensIncluded)
                {
                    exportLogger.Log("Warning: tokensIncluded=false; clients may need to re-login after migration");
                }
            }
            catch (Exception ex)
            {
                ok = false;
                exportLogger.LogError($"Failed to parse devices.json: {ex.Message}", ex);
            }
        }

        // Watch history
        if (!Directory.Exists(watchDir))
        {
            exportLogger.Log("watch_history directory not found; skipping watched-state verification");
        }
        else
        {
            var files = Directory.GetFiles(watchDir, "*.json");
            exportLogger.Log($"Found {files.Length} watch-history file(s)");
        }

        exportLogger.Log(ok ? "VERIFY PASS" : "VERIFY FAIL");
    }
}

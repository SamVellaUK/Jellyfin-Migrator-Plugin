using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportService"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">User manager for accessing user data.</param>
    /// <param name="libraryManager">Library manager for accessing library data.</param>
    /// <param name="userViewManager">User view manager for accessing user libraries.</param>
    public ExportService(IApplicationPaths paths, ILogger<ExportService> logger, IUserManager userManager, ILibraryManager libraryManager, IUserViewManager userViewManager)
    {
        _paths = paths;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userViewManager = userViewManager;
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

        var exportRoot = GetExportDirectory(config);
        Directory.CreateDirectory(exportRoot);

        var exportLogger = new ExportLogger(_logger);
        var userExporter = new UserExporter(_paths, exportLogger, _logger, _userManager, _libraryManager, _userViewManager);

        exportLogger.Log("Migrator export started (simplified mode)");
        exportLogger.Log($"Data path: {_paths.DataPath}");
        exportLogger.Log($"Export directory: {exportRoot}");

        var selectedIds = config.SelectedUserIds ?? new List<string>();
        exportLogger.Log($"Selected user IDs filter: [{string.Join(", ", selectedIds)}] ({(selectedIds.Count == 0 ? "ALL USERS" : $"{selectedIds.Count} specific users")})");

        var outputPath = Path.Combine(exportRoot, "users_basic.json");
        try
        {
            var exportedCount = await userExporter.ExportUsersAsync(outputPath, selectedIds, cancellationToken).ConfigureAwait(false);
            exportLogger.Log($"Export completed successfully. {exportedCount} users exported.");
        }
        catch (Exception ex)
        {
            exportLogger.LogError($"Export failed: {ex.Message}", ex);
        }

        progress?.Report(100);

        // Save logs
        await exportLogger.SaveLogToFileAsync(exportRoot).ConfigureAwait(false);
        exportLogger.SaveToConfiguration(exportRoot);
    }

    private string GetExportDirectory(PluginConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.ExportDirectory))
        {
            return config.ExportDirectory!;
        }

        return Path.Combine(_paths.DataPath, "plugins", "Migrator", "exports", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    }
}

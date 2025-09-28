using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// A scheduled task to run the migration export.
/// </summary>
public class ExportMigrationTask : IScheduledTask
{
    private readonly IApplicationPaths _paths;
    private readonly ILogger<ExportMigrationTask> _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserViewManager _userViewManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportMigrationTask"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">Instance of <see cref="IUserManager"/>.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/>.</param>
    /// <param name="userViewManager">Instance of <see cref="IUserViewManager"/>.</param>
    public ExportMigrationTask(IApplicationPaths paths, ILogger<ExportMigrationTask> logger, IUserManager userManager, ILibraryManager libraryManager, IUserViewManager userViewManager)
    {
        _paths = paths;
        _logger = logger;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userViewManager = userViewManager;
    }

    /// <inheritdoc />
    public string Name => "Migration: Export Data";

    /// <inheritdoc />
    public string Key => "JellyfinMigratorExport";

    /// <inheritdoc />
    public string Description => "Minimal export: users only (scoped down for reliability)";

    /// <inheritdoc />
    public string Category => "Migration";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // No default schedule; run manually from Dashboard → Scheduled Tasks
        return Array.Empty<TaskTriggerInfo>();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var service = new ExportService(_paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExportService>.Instance, _userManager, _libraryManager, _userViewManager);

        try
        {
            await service.RunAsync(cfg, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Export canceled by user.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed: {Message}", ex.Message);
            throw;
        }
    }
}

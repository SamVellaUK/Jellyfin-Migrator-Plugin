using System;
using System.Collections.Generic;
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
/// A scheduled task to run the migration export.
/// </summary>
public class ExportMigrationTask : IScheduledTask
{
    private readonly IApplicationPaths _paths;
    private readonly ILogger<ExportMigrationTask> _logger;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserViewManager _userViewManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IDeviceManager _deviceManager;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportMigrationTask"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userManager">Instance of <see cref="IUserManager"/>.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/>.</param>
    /// <param name="userViewManager">Instance of <see cref="IUserViewManager"/>.</param>
    /// <param name="userDataManager">Instance of <see cref="IUserDataManager"/>.</param>
    /// <param name="deviceManager">Instance of <see cref="IDeviceManager"/>.</param>
    /// <param name="sessionManager">Instance of <see cref="ISessionManager"/>.</param>
    /// <param name="taskManager">Instance of <see cref="ITaskManager"/>.</param>
    public ExportMigrationTask(IApplicationPaths paths, ILogger<ExportMigrationTask> logger, IUserManager userManager, ILibraryManager libraryManager, IUserViewManager userViewManager, IUserDataManager userDataManager, IDeviceManager deviceManager, ISessionManager sessionManager, ITaskManager taskManager)
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

    /// <inheritdoc />
    public string Name => "Migration: Export Data";

    /// <inheritdoc />
    public string Key => "JellyfinMigratorExport";

    /// <inheritdoc />
    public string Description => "Run migration operation (Export/Verify)";

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
        var service = new ExportService(_paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExportService>.Instance, _userManager, _libraryManager, _userViewManager, _userDataManager, _deviceManager, _sessionManager, _taskManager);

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

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Handles logging for export operations.
/// </summary>
public class ExportLogger
{
    private readonly ILogger _logger;
    private readonly StringBuilder _logBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportLogger"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ExportLogger(ILogger logger)
    {
        _logger = logger;
        _logBuilder = new StringBuilder();
    }

    /// <summary>
    /// Logs a message with timestamp.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Log(string message)
    {
        var timestampedMessage = $"{DateTime.Now:HH:mm:ss} {message}";
        _logger.LogInformation("{Message}", timestampedMessage);
        _logBuilder.AppendLine(timestampedMessage);
    }

    /// <summary>
    /// Logs an error message with optional exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="ex">Optional exception details.</param>
    public void LogError(string message, Exception? ex = null)
    {
        var timestampedMessage = $"{DateTime.Now:HH:mm:ss} ERROR: {message}";
        if (ex != null)
        {
            _logger.LogError(ex, "{Message}", timestampedMessage);
            _logBuilder.AppendLine(CultureInfo.InvariantCulture, $"{timestampedMessage}: {ex.Message}");
        }
        else
        {
            _logger.LogError("{Message}", timestampedMessage);
            _logBuilder.AppendLine(timestampedMessage);
        }
    }

    /// <summary>
    /// Gets the complete log as a string.
    /// </summary>
    /// <returns>The full log content.</returns>
    public string GetFullLog() => _logBuilder.ToString();

    /// <summary>
    /// Saves the log to a file in the export directory.
    /// </summary>
    /// <param name="exportRoot">The export root directory.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SaveLogToFileAsync(string exportRoot)
    {
        try
        {
            var logPath = Path.Combine(exportRoot, "export.log");
            await File.WriteAllTextAsync(logPath, GetFullLog()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save log file to {ExportRoot}: {Message}", exportRoot, ex.Message);
        }
    }

    /// <summary>
    /// Saves the log to the plugin configuration.
    /// </summary>
    /// <param name="exportRoot">The export root directory.</param>
    public void SaveToConfiguration(string exportRoot)
    {
        try
        {
            var cfg = Plugin.Instance?.Configuration;
            if (cfg is not null)
            {
                cfg.LastExportLog = GetFullLog();
                cfg.LastExportPath = exportRoot;
                cfg.LastExportUtc = DateTime.UtcNow;
                Plugin.Instance?.SaveConfiguration();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save log to plugin configuration: {Message}", ex.Message);
        }
    }
}

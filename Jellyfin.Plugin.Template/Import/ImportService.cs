using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Import;

/// <summary>
/// Handles receiving a migration ZIP, extracting it securely and analyzing its contents.
/// </summary>
internal sealed class ImportService
{
    private readonly IApplicationPaths _paths;
    private readonly ILogger<ImportService> _logger;

    internal ImportService(IApplicationPaths paths, ILogger<ImportService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Processes an uploaded export ZIP stream: extracts to a timestamped directory and analyzes users/libraries.
    /// </summary>
    /// <param name="zipStream">The ZIP file stream to extract and analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result containing users, libraries and any errors.</returns>
    internal async Task<ImportAnalysisResult> ProcessZipAsync(Stream zipStream, CancellationToken cancellationToken = default)
    {
        var result = new ImportAnalysisResult();

        // Create extraction directory under plugin data path
        var baseDir = Path.Combine(_paths.DataPath, "plugins", "Migrator", "imports");
        Directory.CreateDirectory(baseDir);
        var extractDir = Path.Combine(baseDir, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(extractDir);

        try
        {
            await ExtractZipSecureAsync(zipStream, extractDir, cancellationToken).ConfigureAwait(false);
            result.ExtractedPath = extractDir;
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Import: invalid ZIP format");
            result.Errors.Add("Invalid ZIP file.");
            result.Message = ex.Message;
            result.Ok = false;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import: failed to extract ZIP");
            result.Errors.Add("Failed to extract ZIP: " + ex.Message);
            result.Ok = false;
            return result;
        }

        // Analyze contents
        try
        {
            AnalyzeUsers(extractDir, result, cancellationToken);
            AnalyzeLibraries(extractDir, result, cancellationToken);

            result.Ok = result.Errors.Count == 0;
            if (result.Ok && string.IsNullOrWhiteSpace(result.Message))
            {
                result.Message = "Analysis complete.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import: analysis failed");
            result.Errors.Add("Analysis failed: " + ex.Message);
            result.Ok = false;
        }

        return result;
    }

    private static async Task ExtractZipSecureAsync(Stream zipStream, string destinationDirectory, CancellationToken cancellationToken)
    {
        // Reset stream if possible
        if (zipStream.CanSeek)
        {
            zipStream.Seek(0, SeekOrigin.Begin);
        }

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip directory entries
            if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var sanitized = entry.FullName.Replace('\\', '/');
            if (sanitized.Contains("..", StringComparison.Ordinal))
            {
                // Avoid Zip Slip
                continue;
            }

            var fullPath = Path.Combine(destinationDirectory, sanitized);
            var fullDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(fullDir))
            {
                Directory.CreateDirectory(fullDir);
            }

            using var entryStream = entry.Open();
            using var outStream = File.Create(fullPath);
            await entryStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AnalyzeUsers(string root, ImportAnalysisResult result, CancellationToken cancellationToken)
    {
        var usersPath = Path.Combine(root, "users_basic.json");
        if (!File.Exists(usersPath))
        {
            result.Errors.Add("users_basic.json not found in archive.");
            return;
        }

        using var fs = File.OpenRead(usersPath);
        using var doc = JsonDocument.Parse(fs);
        var rootEl = doc.RootElement;
        if (rootEl.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add("users_basic.json is not a JSON array.");
            return;
        }

        foreach (var el in rootEl.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? id = null;
            string? username = null;

            if (el.TryGetProperty("id", out var idEl))
            {
                id = idEl.GetString();
            }
            else if (el.TryGetProperty("Id", out var idEl2))
            {
                id = idEl2.GetString();
            }

            if (el.TryGetProperty("username", out var unEl))
            {
                username = unEl.GetString();
            }
            else if (el.TryGetProperty("Username", out var unEl2))
            {
                username = unEl2.GetString();
            }

            result.Users.Add(new SimpleUser { Id = id, Username = username });
        }
    }

    private static void AnalyzeLibraries(string root, ImportAnalysisResult result, CancellationToken cancellationToken)
    {
        var libsPath = Path.Combine(root, "libraries.json");
        if (!File.Exists(libsPath))
        {
            result.Errors.Add("libraries.json not found in archive.");
            return;
        }

        using var fs = File.OpenRead(libsPath);
        using var doc = JsonDocument.Parse(fs);
        var rootEl = doc.RootElement;
        if (rootEl.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add("libraries.json is not a JSON array.");
            return;
        }

        foreach (var el in rootEl.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? id = null;
            string? name = null;

            if (el.TryGetProperty("Id", out var idEl) || el.TryGetProperty("id", out idEl))
            {
                id = idEl.GetString();
            }

            if (el.TryGetProperty("Name", out var nameEl) || el.TryGetProperty("name", out nameEl))
            {
                name = nameEl.GetString();
            }

            result.Libraries.Add(new SimpleLibrary { Id = id, Name = name });
        }
    }

    internal sealed class ImportAnalysisResult
    {
        public bool Ok { get; set; }

        public string? Message { get; set; }

        public string? ExtractedPath { get; set; }

        public List<SimpleUser> Users { get; set; } = new List<SimpleUser>();

        public List<SimpleLibrary> Libraries { get; set; } = new List<SimpleLibrary>();

        public List<string> Errors { get; set; } = new List<string>();
    }

    internal sealed class SimpleUser
    {
        public string? Id { get; set; }

        public string? Username { get; set; }
    }

    internal sealed class SimpleLibrary
    {
        public string? Id { get; set; }

        public string? Name { get; set; }
    }
}

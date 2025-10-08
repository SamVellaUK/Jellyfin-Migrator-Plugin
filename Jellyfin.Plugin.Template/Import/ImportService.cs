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

    private void AnalyzeUsers(string root, ImportAnalysisResult result, CancellationToken cancellationToken)
    {
        var usersPath = Path.Combine(root, "users_basic.json");
        if (!File.Exists(usersPath))
        {
            _logger.LogWarning("Import: users_basic.json not found in archive at {Path}", usersPath);
            result.Errors.Add("users_basic.json not found in archive.");
            return;
        }

        _logger.LogInformation("Import: Analyzing users from {Path}", usersPath);

        using var fs = File.OpenRead(usersPath);
        using var doc = JsonDocument.Parse(fs);
        var rootEl = doc.RootElement;
        if (rootEl.ValueKind != JsonValueKind.Array)
        {
            _logger.LogError("Import: users_basic.json is not a JSON array");
            result.Errors.Add("users_basic.json is not a JSON array.");
            return;
        }

        var userCount = 0;
        foreach (var el in rootEl.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user = new ImportUser { Selected = true };

            // ID
            if (el.TryGetProperty("id", out var idEl) || el.TryGetProperty("Id", out idEl))
            {
                user.Id = idEl.GetString();
            }

            // Username
            if (el.TryGetProperty("username", out var unEl) || el.TryGetProperty("Username", out unEl))
            {
                user.Username = unEl.GetString();
            }

            // IsAdministrator
            if (el.TryGetProperty("isAdministrator", out var adminEl) || el.TryGetProperty("IsAdministrator", out adminEl))
            {
                user.IsAdministrator = adminEl.GetBoolean();
            }
            else if (el.TryGetProperty("policy", out var policyEl) && policyEl.TryGetProperty("isAdministrator", out var pAdminEl))
            {
                user.IsAdministrator = pAdminEl.GetBoolean();
            }

            // IsDisabled
            if (el.TryGetProperty("isDisabled", out var disEl) || el.TryGetProperty("IsDisabled", out disEl))
            {
                user.IsDisabled = disEl.GetBoolean();
            }
            else if (el.TryGetProperty("policy", out var policyEl2) && policyEl2.TryGetProperty("isDisabled", out var pDisEl))
            {
                user.IsDisabled = pDisEl.GetBoolean();
            }

            // Password Hash
            if (el.TryGetProperty("passwordHash", out var pwEl) || el.TryGetProperty("PasswordHash", out pwEl))
            {
                user.PasswordHash = pwEl.GetString();
            }

            // Libraries
            if (el.TryGetProperty("libraries", out var libsEl) || el.TryGetProperty("Libraries", out libsEl))
            {
                if (libsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var libEl in libsEl.EnumerateArray())
                    {
                        if (libEl.TryGetProperty("id", out var libIdEl) || libEl.TryGetProperty("Id", out libIdEl))
                        {
                            var libId = libIdEl.GetString();
                            if (!string.IsNullOrWhiteSpace(libId))
                            {
                                user.LibraryIds.Add(libId);
                            }
                        }

                        if (libEl.TryGetProperty("name", out var libNameEl) || libEl.TryGetProperty("Name", out libNameEl))
                        {
                            var libName = libNameEl.GetString();
                            if (!string.IsNullOrWhiteSpace(libName))
                            {
                                user.LibraryNames.Add(libName);
                            }
                        }
                    }
                }
            }

            result.Users.Add(user);
            userCount++;
            _logger.LogDebug("Import: Parsed user {Username} (ID: {Id}, Admin: {IsAdmin}, Disabled: {IsDisabled}, Libraries: {LibCount})", user.Username, user.Id, user.IsAdministrator, user.IsDisabled, user.LibraryIds.Count);
        }

        _logger.LogInformation("Import: Successfully parsed {Count} users", userCount);
    }

    private void AnalyzeLibraries(string root, ImportAnalysisResult result, CancellationToken cancellationToken)
    {
        var libsPath = Path.Combine(root, "libraries.json");
        if (!File.Exists(libsPath))
        {
            _logger.LogWarning("Import: libraries.json not found in archive at {Path}", libsPath);
            result.Errors.Add("libraries.json not found in archive.");
            return;
        }

        _logger.LogInformation("Import: Analyzing libraries from {Path}", libsPath);

        using var fs = File.OpenRead(libsPath);
        using var doc = JsonDocument.Parse(fs);
        var rootEl = doc.RootElement;
        if (rootEl.ValueKind != JsonValueKind.Array)
        {
            _logger.LogError("Import: libraries.json is not a JSON array");
            result.Errors.Add("libraries.json is not a JSON array.");
            return;
        }

        var libCount = 0;
        foreach (var el in rootEl.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var library = new ImportLibrary { Selected = true };

            // ID
            if (el.TryGetProperty("Id", out var idEl) || el.TryGetProperty("id", out idEl))
            {
                library.Id = idEl.GetString();
            }

            // Name
            if (el.TryGetProperty("Name", out var nameEl) || el.TryGetProperty("name", out nameEl))
            {
                library.Name = nameEl.GetString();
            }

            // CollectionType
            if (el.TryGetProperty("CollectionType", out var typeEl) || el.TryGetProperty("collectionType", out typeEl))
            {
                library.CollectionType = typeEl.GetString();
            }

            // Locations
            if (el.TryGetProperty("Locations", out var locsEl) || el.TryGetProperty("locations", out locsEl))
            {
                if (locsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var locEl in locsEl.EnumerateArray())
                    {
                        var loc = locEl.GetString();
                        if (!string.IsNullOrWhiteSpace(loc))
                        {
                            library.Locations.Add(loc);
                        }
                    }
                }
            }

            result.Libraries.Add(library);
            libCount++;
            _logger.LogDebug("Import: Parsed library {Name} (ID: {Id}, Type: {Type}, Locations: {LocCount})", library.Name, library.Id, library.CollectionType, library.Locations.Count);
        }

        _logger.LogInformation("Import: Successfully parsed {Count} libraries", libCount);
    }

    internal sealed class ImportAnalysisResult
    {
        public bool Ok { get; set; }

        public string? Message { get; set; }

        public string? ExtractedPath { get; set; }

        public List<ImportUser> Users { get; set; } = new List<ImportUser>();

        public List<ImportLibrary> Libraries { get; set; } = new List<ImportLibrary>();

        public List<string> Errors { get; set; } = new List<string>();
    }

    internal sealed class ImportUser
    {
        public string? Id { get; set; }

        public string? Username { get; set; }

        public bool IsAdministrator { get; set; }

        public bool IsDisabled { get; set; }

        public string? PasswordHash { get; set; }

        public List<string> LibraryIds { get; set; } = new List<string>();

        public List<string> LibraryNames { get; set; } = new List<string>();

        public bool Selected { get; set; }
    }

    internal sealed class ImportLibrary
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? CollectionType { get; set; }

        public List<string> Locations { get; set; } = new List<string>();

        public bool Selected { get; set; }
    }
}

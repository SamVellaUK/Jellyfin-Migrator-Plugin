using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Template.Export;

/// <summary>
/// Exports library (virtual folder) definitions with their settings.
/// </summary>
public class LibraryDefinitionsExporter
{
    private readonly IApplicationPaths _paths;
    private readonly ExportLogger _exportLogger;
    private readonly ILibraryManager _libraryManager;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryDefinitionsExporter"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="exportLogger">Export logger instance.</param>
    /// <param name="libraryManager">Library manager service.</param>
    public LibraryDefinitionsExporter(IApplicationPaths paths, ExportLogger exportLogger, ILibraryManager libraryManager)
    {
        _paths = paths;
        _exportLogger = exportLogger;
        _libraryManager = libraryManager;
}

    /// <summary>
    /// Export all library definitions to the specified path.
    /// </summary>
    /// <param name="outputPath">Destination JSON file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of libraries exported.</returns>
    public async Task<int> ExportAsync(string outputPath, CancellationToken cancellationToken)
    {
        _exportLogger.Log("Starting library definitions export");

        var folders = _libraryManager.GetVirtualFolders();
        var list = new List<LibraryDefinitionExport>();

        foreach (var vf in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var idStr = vf.ItemId ?? string.Empty;
            var parsed = Guid.TryParse(idStr, out var id);
            var rootItem = parsed && id != Guid.Empty ? _libraryManager.GetItemById(id) : null;

            // Try to read common root item fields
            var rootPath = TryGetString(rootItem, "Path");
            var rootPaths = TryGetEnumerable(rootItem, "Paths")?.OfType<string>().ToList();

            // Try to extract library options via multiple strategies to ensure we capture settings
            var optionsElement = TryGetLibraryOptionsAsJson(vf, rootItem, id);
            if (optionsElement is null)
            {
                optionsElement = TryGetLibraryOptionsViaManager(id, vf.Name, rootItem);
            }

            list.Add(new LibraryDefinitionExport
            {
                Id = id.ToString("N", CultureInfo.InvariantCulture),
                IdDashed = id.ToString("D", CultureInfo.InvariantCulture),
                Name = vf.Name,
                CollectionType = vf.CollectionType?.ToString(),
                Locations = vf.Locations,
                PrimaryImageItemId = vf.PrimaryImageItemId,
                RootPath = rootPath,
                RootPaths = rootPaths,
                Options = optionsElement
            });
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(fs, list, JsonOptions, cancellationToken).ConfigureAwait(false);

        _exportLogger.Log($"Exported {list.Count} libraries with settings -> {outputPath}");
        return list.Count;
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

    private static System.Collections.IEnumerable? TryGetEnumerable(object? obj, string propertyName)
    {
        var v = TryGetProperty(obj, propertyName);
        return v as System.Collections.IEnumerable;
    }

    private static JsonElement? TryGetLibraryOptionsAsJson(object? virtualFolder, object? rootItem, Guid rootId)
    {
        // Strategy A: Look for 'LibraryOptions' property on VirtualFolderInfo (public or non-public)
        object? optionsObj = TryGetProperty(virtualFolder, "LibraryOptions");
        if (optionsObj is null)
        {
            // Strategy B: Look for 'LibraryOptions' on the root item (CollectionFolder often exposes this)
            optionsObj = TryGetProperty(rootItem, "LibraryOptions");
        }

        // If we found an options object, serialize it to a stable JsonElement
        if (optionsObj is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(optionsObj, JsonOptions);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                // fall through to null
            }
        }

        return null;
    }

    private JsonElement? TryGetLibraryOptionsViaManager(Guid id, string? name, object? rootItem)
    {
        // Attempt to call ILibraryManager.GetLibraryOptions using dynamic invocation to handle API differences.
        try
        {
            dynamic lm = _libraryManager;
            try
            {
                var o = lm.GetLibraryOptions(id);
                return SerializeToElement(o);
            }
            catch
            {
                // ignore and try other overloads
            }

            try
            {
                var o = lm.GetLibraryOptions(id.ToString("N", CultureInfo.InvariantCulture));
                return SerializeToElement(o);
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    var o = lm.GetLibraryOptions(name);
                    return SerializeToElement(o);
                }
                catch
                {
                    // ignore
                }
            }

            if (rootItem is not null)
            {
                try
                {
                    var o = lm.GetLibraryOptions(rootItem);
                    return SerializeToElement(o);
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch
        {
            // dynamic/overload resolution failed; return null
        }

        return null;
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

    private sealed class LibraryDefinitionExport
    {
        public string Id { get; set; } = string.Empty;

        public string IdDashed { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? CollectionType { get; set; }

        public IEnumerable<string>? Locations { get; set; }

        public string? PrimaryImageItemId { get; set; }

        public string? RootPath { get; set; }

        public List<string>? RootPaths { get; set; }

        public JsonElement? Options { get; set; }
    }
}

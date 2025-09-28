using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Template.Configuration;

/// <summary>
/// Plugin configuration for export selections and options.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class with defaults.
    /// </summary>
    public PluginConfiguration()
    {
        ExportDirectory = string.Empty;

        IncludeUsers = true;
        IncludeUserPasswordHashes = false;
        IncludeLibraries = true;
        IncludePermissions = true;
        IncludeWatchHistory = true;
        IncludeDevices = true;

        SelectedUserIds = new List<string>();
        SelectedUsernames = new List<string>();
        SelectedLibraryIds = new List<string>();
        SelectedLibraryPaths = new List<string>();
    }

    /// <summary>
    /// Gets or sets the export directory for generated JSON files. If empty, a default under the Jellyfin data path is used.
    /// </summary>
    public string ExportDirectory { get; set; }

    // No server URL or API key required; plugin runs in-process.

    /// <summary>
    /// Gets or sets a value indicating whether to export users (via REST) limited to selected users.
    /// </summary>
    public bool IncludeUsers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include password hashes from the database for selected users.
    /// </summary>
    public bool IncludeUserPasswordHashes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to export libraries (via REST) limited to selected libraries.
    /// </summary>
    public bool IncludeLibraries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to export user permissions (via REST) for selected users/libraries.
    /// </summary>
    public bool IncludePermissions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to export watch history (from SQLite) filtered by selected users/libraries.
    /// </summary>
    public bool IncludeWatchHistory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to export devices (from SQLite) filtered by selected users.
    /// </summary>
    public bool IncludeDevices { get; set; }

    /// <summary>
    /// Gets or sets selected user IDs (for REST filtering).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Configuration needs settable collection for serialization")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Used for configuration serialization")]
    public List<string> SelectedUserIds { get; set; }

    /// <summary>
    /// Gets or sets selected usernames (for SQLite filtering).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Configuration needs settable collection for serialization")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Used for configuration serialization")]
    public List<string> SelectedUsernames { get; set; }

    /// <summary>
    /// Gets or sets selected library IDs (for REST filtering).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Configuration needs settable collection for serialization")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Used for configuration serialization")]
    public List<string> SelectedLibraryIds { get; set; }

    /// <summary>
    /// Gets or sets selected library paths (for SQLite filtering of watched history).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Configuration needs settable collection for serialization")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Used for configuration serialization")]
    public List<string> SelectedLibraryPaths { get; set; }

    /// <summary>
    /// Gets or sets the last export log text (short diagnostic shown on the config page).
    /// </summary>
    public string? LastExportLog { get; set; }

    /// <summary>
    /// Gets or sets the path of the last export directory.
    /// </summary>
    public string? LastExportPath { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last export completion.
    /// </summary>
    public DateTime? LastExportUtc { get; set; }
}

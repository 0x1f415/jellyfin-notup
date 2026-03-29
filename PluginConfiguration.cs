using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NextUpFilter;

/// <summary>
/// Persisted settings for the Next Up Filter plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Item IDs of TV series that should never appear in "Next Up".
    /// Each element is a GUID string, e.g. "3fa85f64-5717-4562-b3fc-2c963f66afa6".
    /// </summary>
    public string[] ExcludedSeriesIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional.  The item ID of a Jellyfin playlist whose contents are used as an
    /// additional exclusion source.  Any series represented by items in this playlist
    /// (series directly, or episodes whose parent series is resolved) will be hidden
    /// from "Next Up" at request time.
    /// </summary>
    public string ExclusionPlaylistId { get; set; } = string.Empty;
}

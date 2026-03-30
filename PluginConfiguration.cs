using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NextUpFilter;

/// <summary>
/// Top-level plugin configuration.  Filter settings are stored per-user so
/// that each account on the server can maintain its own exclusion list.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Per-user filter settings keyed by user ID (lowercase GUID string,
    /// e.g. "3fa85f64-5717-4562-b3fc-2c963f66afa6").
    /// </summary>
    public Dictionary<string, UserFilterConfig> UserSettings { get; set; } = new();
}

/// <summary>
/// Filter settings for a single Jellyfin user.
/// </summary>
public class UserFilterConfig
{
    /// <summary>
    /// Ordered list of items to exclude from Next Up.
    /// Each entry is either a Series (excluded directly), a Playlist, or a
    /// BoxSet (Collection) whose contents are resolved to series at request time.
    /// </summary>
    public ExclusionEntry[] ExclusionItems { get; set; } = Array.Empty<ExclusionEntry>();
}

/// <summary>One entry in a user's exclusion list.</summary>
public class ExclusionEntry
{
    /// <summary>"Series", "Playlist", or "BoxSet".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Jellyfin item ID (GUID string).</summary>
    public string Id { get; set; } = string.Empty;
}

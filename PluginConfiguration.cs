using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NextUpFilter;

/// <summary>
/// Top-level plugin configuration.  Filter settings are stored per-user so
/// that each account on the server can maintain its own exclusion list.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Per-user filter settings.  Stored as a flat array (rather than a
    /// Dictionary) because XmlSerializer cannot serialize IDictionary types.
    /// Keyed logically by <see cref="UserSetting.UserId"/> (lowercase GUID).
    /// </summary>
    public UserSetting[] UserSettings { get; set; } = Array.Empty<UserSetting>();
}

/// <summary>
/// Filter settings for a single Jellyfin user.
/// </summary>
public class UserSetting
{
    /// <summary>Lowercase GUID string identifying the Jellyfin user.</summary>
    public string UserId { get; set; } = string.Empty;

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

    /// <summary>Display name (stored so the config page can render chips without re-fetching).</summary>
    public string Name { get; set; } = string.Empty;
}

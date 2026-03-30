using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.NextUpFilter;

/// <summary>
/// Main plugin class – registered automatically by Jellyfin on startup.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    // Stable GUID that must never change between releases.
    public static readonly Guid PluginGuid = new("a8e7d6c5-b4a3-4e2f-9d1b-c0e8f7a6d5b4");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Singleton reference set during construction.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Next Up Filter";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <inheritdoc />
    public override string Description =>
        "Hide selected series (and/or a playlist's contents) from the Next Up list.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name                 = Name,
            DisplayName          = "Next Up Filter",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            EnableInMainMenu     = true,
            MenuSection          = "server",
            MenuIcon             = "filter_list",
        };
    }
}

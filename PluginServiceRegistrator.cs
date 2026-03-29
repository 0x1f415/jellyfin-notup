using Jellyfin.Plugin.NextUpFilter.Middleware;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.NextUpFilter;

/// <summary>
/// Registers the middleware that intercepts /Shows/NextUp responses.
/// Jellyfin discovers this class via reflection and calls <see cref="RegisterServices"/>
/// during application startup.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // IStartupFilter is the standard ASP.NET Core hook for prepending middleware.
        serviceCollection.AddTransient<IStartupFilter, NextUpFilterStartupFilter>();
    }
}

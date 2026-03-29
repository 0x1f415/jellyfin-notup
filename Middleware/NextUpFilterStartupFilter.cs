using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.NextUpFilter.Middleware;

/// <summary>
/// Prepends <see cref="NextUpFilterMiddleware"/> to the ASP.NET Core pipeline
/// before Jellyfin's own routing middleware executes.
/// </summary>
public sealed class NextUpFilterStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<NextUpFilterMiddleware>();
            next(app);
        };
    }
}

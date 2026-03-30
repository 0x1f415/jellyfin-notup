using System.Text;
using System.Text.Json;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NextUpFilter.Middleware;

/// <summary>
/// Intercepts GET /Shows/NextUp responses and removes episodes whose parent
/// series appear in the plugin's exclusion lists (manually configured IDs and/or
/// the contents of a nominated playlist).
/// </summary>
/// <remarks>
/// Pagination strategy (fetch-ahead stitching):
///   The original limit/startIndex are captured and replaced with limit=<see cref="FetchAheadLimit"/>
///   startIndex=0 before the request is forwarded to Jellyfin's own handler.
///   Jellyfin does its normal work on the full dataset; the middleware then filters
///   the enlarged batch, applies the original startIndex/limit as a slice, and
///   sets TotalRecordCount to the filtered total.  This means pagination is always
///   correct regardless of how many series are excluded.
///
///   Other notes:
///   • Accept-Encoding is stripped so the upstream response is uncompressed JSON.
///   • Playlist exclusions are resolved per-request; changes take effect immediately.
///   • On any JSON parse error the original body is forwarded unchanged.
/// </remarks>
public sealed class NextUpFilterMiddleware
{
    /// <summary>
    /// How many items to fetch from Jellyfin before filtering.
    /// Next Up is bounded by the number of in-progress series, so 2 000 is a
    /// generous ceiling that keeps memory usage trivial.
    /// </summary>
    private const int FetchAheadLimit = 2000;

    private readonly RequestDelegate _next;
    private readonly ILogger<NextUpFilterMiddleware> _logger;

    public NextUpFilterMiddleware(RequestDelegate next, ILogger<NextUpFilterMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ILibraryManager libraryManager)
    {
        // ── 1. Only handle Next Up requests ──────────────────────────────────────
        if (!IsNextUpRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            await _next(context);
            return;
        }

        // ── 2. Build the set of excluded series GUIDs ─────────────────────────────
        var config      = plugin.Configuration;

        _logger.LogInformation(
            "NextUpFilter: intercepted NextUp request. UserSettings count={Count}, query={Query}",
            config.UserSettings.Length,
            context.Request.QueryString);

        var excludedIds = BuildExclusionSet(config, libraryManager, context.Request);

        _logger.LogInformation(
            "NextUpFilter: exclusion set resolved to {Count} series IDs: [{Ids}]",
            excludedIds.Count,
            string.Join(", ", excludedIds));

        if (excludedIds.Count == 0)
        {
            await _next(context);
            return;
        }

        // ── 3. Capture original pagination, replace with fetch-ahead values ───────
        var originalQuery  = context.Request.Query;
        int origLimit      = ParseIntParam(originalQuery, "limit",      20);
        int origStartIndex = ParseIntParam(originalQuery, "startIndex", 0);

        // Rewrite query string: fetch everything from the beginning.
        context.Request.QueryString = ReplaceQueryParams(
            context.Request.QueryString,
            ("limit",      FetchAheadLimit.ToString()),
            ("startIndex", "0"));

        // ── 4. Prevent upstream compression so we can read raw JSON ───────────────
        context.Request.Headers.Remove("Accept-Encoding");

        // ── 5. Swap the response body stream with a buffer ────────────────────────
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body  = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        // ── 6. Only rewrite successful JSON responses ─────────────────────────────
        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            return;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var rawJson = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

        var filteredJson = FilterNextUpJson(rawJson, excludedIds, origStartIndex, origLimit);

        var filteredBytes = Encoding.UTF8.GetBytes(filteredJson);
        context.Response.ContentLength = filteredBytes.Length;
        await originalBody.WriteAsync(filteredBytes);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static bool IsNextUpRequest(HttpRequest request)
    {
        return request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && request.Path.Value is { } path
            && path.Contains("/Shows/NextUp", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseIntParam(IQueryCollection query, string key, int fallback)
    {
        return query.TryGetValue(key, out var sv)
            && int.TryParse(sv.FirstOrDefault(), out var v) ? v : fallback;
    }

    /// <summary>
    /// Returns a new <see cref="QueryString"/> with the given key/value pairs
    /// overwritten; all other parameters are preserved.
    /// </summary>
    private static QueryString ReplaceQueryParams(QueryString original, params (string key, string value)[] overrides)
    {
        // Parse existing params into a mutable dict.
        var dict = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(original.Value ?? string.Empty);

        foreach (var (k, v) in overrides)
            dict[k] = v;

        var sb = new System.Text.StringBuilder("?");
        foreach (var kv in dict)
        {
            foreach (var val in kv.Value)
            {
                if (sb.Length > 1) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(val ?? string.Empty));
            }
        }
        return new QueryString(sb.ToString());
    }

    /// <summary>
    /// Returns the exclusion set for the user identified by the <c>userId</c>
    /// query parameter.  Returns an empty set when no per-user config exists.
    /// </summary>
    private HashSet<Guid> BuildExclusionSet(
        PluginConfiguration  config,
        ILibraryManager      libraryManager,
        HttpRequest          request)
    {
        // Keys are stored as lowercase GUIDs — bail if we can't identify the user.
        if (!request.Query.TryGetValue("userId", out var rawUid)
            || !Guid.TryParse(rawUid.FirstOrDefault(), out var userId))
        {
            return new HashSet<Guid>();
        }

        var key = userId.ToString("N").ToLowerInvariant(); // "N" = no dashes, matches Jellyfin's frontend user ID format
        var userConfig = Array.Find(config.UserSettings, s => s.UserId == key);

        _logger.LogInformation(
            "NextUpFilter: looking up user key={Key}, found={Found}, all keys=[{AllKeys}]",
            key,
            userConfig is not null,
            string.Join(", ", config.UserSettings.Select(s => s.UserId)));

        if (userConfig is null)
            return new HashSet<Guid>();

        _logger.LogInformation(
            "NextUpFilter: user has {Count} exclusion entries: [{Entries}]",
            userConfig.ExclusionItems.Length,
            string.Join(", ", userConfig.ExclusionItems.Select(e => $"{e.Type}:{e.Id}:{e.Name}")));

        var ids = new HashSet<Guid>();

        foreach (var entry in userConfig.ExclusionItems)
        {
            if (!Guid.TryParse(entry.Id, out var itemId))
                continue;

            switch (entry.Type)
            {
                case "Series":
                    ids.Add(itemId);
                    break;

                case "Playlist":
                case "BoxSet": // Jellyfin Collections
                    AddSeriesFromContainer(ids, itemId, libraryManager);
                    break;
            }
        }

        return ids;
    }

    /// <summary>
    /// Walks the direct children of a container (Playlist or Collection/BoxSet)
    /// and adds the parent series GUID for every TV item found inside.
    /// Handles Series, Season, and Episode children.
    /// </summary>
    private void AddSeriesFromContainer(
        HashSet<Guid>    ids,
        Guid             containerId,
        ILibraryManager  libraryManager)
    {
        try
        {
            var result = libraryManager.GetItemsResult(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                ParentId  = containerId,
                Recursive = false,
            });

            foreach (var item in result.Items)
            {
                switch (item)
                {
                    case Series s:
                        ids.Add(s.Id);
                        break;

                    case Season season:
                        if (libraryManager.GetItemById(season.ParentId) is Series seasonParent)
                            ids.Add(seasonParent.Id);
                        break;

                    case Episode ep:
                        var seriesId = ep.SeriesId;
                        if (seriesId != Guid.Empty)
                        {
                            ids.Add(seriesId);
                        }
                        else if (libraryManager.GetItemById(ep.ParentId) is Season epSeason
                              && libraryManager.GetItemById(epSeason.ParentId) is Series epSeries)
                        {
                            ids.Add(epSeries.Id);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextUpFilter: failed to resolve container {ContainerId}", containerId);
        }
    }

    /// <summary>
    /// Parses the full fetch-ahead JSON, filters excluded series, then slices the
    /// result to honour the client's original <paramref name="startIndex"/> /
    /// <paramref name="limit"/>.  Returns the rewritten JSON, or the original on
    /// any parse error.
    /// </summary>
    private string FilterNextUpJson(
        string          json,
        HashSet<Guid>   excludedSeriesIds,
        int             startIndex,
        int             limit)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            if (!root.TryGetProperty("Items", out var itemsEl))
                return json; // Unexpected shape – pass through.

            // ── Filter ───────────────────────────────────────────────────────────
            var filtered = new List<JsonElement>();
            foreach (var item in itemsEl.EnumerateArray())
            {
                if (!ShouldExclude(item, excludedSeriesIds))
                    filtered.Add(item.Clone()); // Clone before doc is disposed.
            }

            // ── Slice to requested page ───────────────────────────────────────────
            int totalFiltered = filtered.Count;
            var page = filtered
                .Skip(startIndex)
                .Take(limit)
                .ToList();

            // ── Rebuild JSON ──────────────────────────────────────────────────────
            using var ms     = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();

            writer.WritePropertyName("Items");
            writer.WriteStartArray();
            foreach (var el in page)
                el.WriteTo(writer);
            writer.WriteEndArray();

            writer.WriteNumber("TotalRecordCount", totalFiltered);
            writer.WriteNumber("StartIndex",       startIndex);
            writer.WriteBoolean("NextUpFilterActive", true);

            writer.WriteEndObject();
            writer.Flush();

            _logger.LogDebug(
                "NextUpFilter: {Total} total → {Filtered} after filtering → returning [{Start}..{End}]",
                totalFiltered + excludedSeriesIds.Count, totalFiltered,
                startIndex, startIndex + page.Count - 1);

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextUpFilter: failed to parse/filter Next Up JSON – passing through");
            return json;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the episode's parent series is in the exclusion set.
    /// </summary>
    private static bool ShouldExclude(JsonElement item, HashSet<Guid> excludedSeriesIds)
    {
        // Episodes carry a SeriesId property.
        if (item.TryGetProperty("SeriesId", out var seriesIdProp)
            && Guid.TryParse(seriesIdProp.GetString(), out var seriesId)
            && excludedSeriesIds.Contains(seriesId))
        {
            return true;
        }

        // Guard against series items appearing directly (unlikely but safe).
        if (item.TryGetProperty("Id", out var idProp)
            && Guid.TryParse(idProp.GetString(), out var id)
            && excludedSeriesIds.Contains(id))
        {
            return true;
        }

        return false;
    }
}

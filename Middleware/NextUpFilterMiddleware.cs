using System.Text;
using System.Text.Json;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NextUpFilter.Middleware;

/// <summary>
/// Intercepts GET /Shows/NextUp responses and filters out episodes according to
/// each user's exclusion list.
/// </summary>
/// <remarks>
/// Exclusion semantics:
///   • Series   — every Next Up episode whose SeriesId matches is removed.
///   • Playlist / BoxSet — only episodes whose Id appears directly in the
///     container's linked-children list are removed.  The series is NOT
///     implicitly excluded; use a Series entry for that.
///
/// Pagination strategy (fetch-ahead stitching):
///   The original limit/startIndex are captured and replaced with
///   limit=<see cref="FetchAheadLimit"/> startIndex=0 before the request is
///   forwarded to Jellyfin.  The middleware then filters the enlarged batch,
///   applies the original startIndex/limit as a slice, and sets
///   TotalRecordCount to the filtered total.
///
///   Other notes:
///   • Accept-Encoding is stripped so the upstream response is plain JSON.
///   • Container contents are resolved per-request; changes take effect immediately.
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

        // ── 2. Build exclusion sets ───────────────────────────────────────────────
        var config = plugin.Configuration;
        var (excludedSeriesIds, excludedItemIds) = BuildExclusionSets(config, libraryManager, context.Request);

        if (excludedSeriesIds.Count == 0 && excludedItemIds.Count == 0)
        {
            await _next(context);
            return;
        }

        // ── 3. Capture original pagination, replace with fetch-ahead values ───────
        int origLimit      = ParseIntParam(context.Request.Query, "limit",      20);
        int origStartIndex = ParseIntParam(context.Request.Query, "startIndex", 0);

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

        var filteredJson = FilterNextUpJson(rawJson, excludedSeriesIds, excludedItemIds, origStartIndex, origLimit);

        var filteredBytes = Encoding.UTF8.GetBytes(filteredJson);
        context.Response.ContentLength = filteredBytes.Length;
        await originalBody.WriteAsync(filteredBytes);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static bool IsNextUpRequest(HttpRequest request)
        => request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        && request.Path.Value is { } path
        && path.Contains("/Shows/NextUp", StringComparison.OrdinalIgnoreCase);

    private static int ParseIntParam(IQueryCollection query, string key, int fallback)
        => query.TryGetValue(key, out var sv)
        && int.TryParse(sv.FirstOrDefault(), out var v) ? v : fallback;

    private static QueryString ReplaceQueryParams(QueryString original, params (string key, string value)[] overrides)
    {
        var dict = QueryHelpers.ParseQuery(original.Value ?? string.Empty);
        foreach (var (k, v) in overrides)
            dict[k] = v;

        var sb = new StringBuilder("?");
        foreach (var kv in dict)
        foreach (var val in kv.Value)
        {
            if (sb.Length > 1) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(val ?? string.Empty));
        }
        return new QueryString(sb.ToString());
    }

    /// <summary>
    /// Returns two sets:
    /// <list type="bullet">
    ///   <item><c>seriesIds</c>  — series GUIDs from direct Series entries.</item>
    ///   <item><c>itemIds</c>    — item GUIDs collected from Playlist/BoxSet linked children.</item>
    /// </list>
    /// </summary>
    private (HashSet<Guid> seriesIds, HashSet<Guid> itemIds) BuildExclusionSets(
        PluginConfiguration config,
        ILibraryManager     libraryManager,
        HttpRequest         request)
    {
        var seriesIds = new HashSet<Guid>();
        var itemIds   = new HashSet<Guid>();

        if (!request.Query.TryGetValue("userId", out var rawUid)
            || !Guid.TryParse(rawUid.FirstOrDefault(), out var userId))
            return (seriesIds, itemIds);

        var key        = userId.ToString("N").ToLowerInvariant();
        var userConfig = Array.Find(config.UserSettings, s => s.UserId == key);

        if (userConfig is null)
            return (seriesIds, itemIds);

        foreach (var entry in userConfig.ExclusionItems)
        {
            if (!Guid.TryParse(entry.Id, out var itemId))
                continue;

            switch (entry.Type)
            {
                case "Series":
                    seriesIds.Add(itemId);
                    break;

                case "Playlist":
                case "BoxSet":
                    AddLinkedItemIds(itemIds, itemId, libraryManager);
                    break;
            }
        }

        return (seriesIds, itemIds);
    }

    /// <summary>
    /// Adds the IDs of all linked children of a Playlist or BoxSet directly to
    /// <paramref name="ids"/>.  No series resolution is performed — the caller
    /// matches against the episode's own <c>Id</c>, not its <c>SeriesId</c>.
    /// </summary>
    private void AddLinkedItemIds(HashSet<Guid> ids, Guid containerId, ILibraryManager libraryManager)
    {
        try
        {
            var container = libraryManager.GetItemById(containerId);
            if (container is null)
            {
                _logger.LogWarning("NextUpFilter: container {Id} not found in library", containerId);
                return;
            }

            if (container is not MediaBrowser.Controller.Entities.Folder folder)
            {
                _logger.LogWarning("NextUpFilter: container {Id} is {Type}, not a Folder — cannot read linked children", containerId, container.GetType().Name);
                return;
            }

            var linked = folder.LinkedChildren;
            foreach (var child in linked)
            {
                if (child.ItemId.HasValue)
                    ids.Add(child.ItemId.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextUpFilter: failed to read linked children of container {Id}", containerId);
        }
    }

    private string FilterNextUpJson(
        string        json,
        HashSet<Guid> excludedSeriesIds,
        HashSet<Guid> excludedItemIds,
        int           startIndex,
        int           limit)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            if (!root.TryGetProperty("Items", out var itemsEl))
                return json;

            var filtered = new List<JsonElement>();
            foreach (var item in itemsEl.EnumerateArray())
            {
                if (!ShouldExclude(item, excludedSeriesIds, excludedItemIds))
                    filtered.Add(item.Clone());
            }

            int totalFiltered = filtered.Count;
            var page = filtered.Skip(startIndex).Take(limit).ToList();

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

            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NextUpFilter: failed to parse/filter Next Up JSON – passing through");
            return json;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when a Next Up episode should be hidden.
    /// <list type="bullet">
    ///   <item>Series exclusions  — matched via the episode's <c>SeriesId</c>.</item>
    ///   <item>Playlist/BoxSet exclusions — matched via the episode's own <c>Id</c>.</item>
    /// </list>
    /// </summary>
    private static bool ShouldExclude(
        JsonElement   item,
        HashSet<Guid> excludedSeriesIds,
        HashSet<Guid> excludedItemIds)
    {
        if (excludedSeriesIds.Count > 0
            && item.TryGetProperty("SeriesId", out var seriesIdProp)
            && Guid.TryParse(seriesIdProp.GetString(), out var seriesId)
            && excludedSeriesIds.Contains(seriesId))
            return true;

        if (excludedItemIds.Count > 0
            && item.TryGetProperty("Id", out var idProp)
            && Guid.TryParse(idProp.GetString(), out var id)
            && excludedItemIds.Contains(id))
            return true;

        return false;
    }
}

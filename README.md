# Jellyfin.Plugin.NextUpFilter

> **Disclaimer:** This project is 100% vibe-coded. No support is provided, no guarantees are made, and no issues will be addressed. Use at your own risk.

A Jellyfin plugin that removes selected TV series from the **"Next Up"** list without touching your watch history.

---

## Features

| Feature | Details |
|---|---|
| **Manual exclusion list** | Add any series by name search in the admin config page; their IDs are stored and matched at request time. |
| **Playlist-driven exclusion** | Point the plugin at any Jellyfin playlist. Items in the playlist (series, seasons, or episodes) are resolved to their parent series and excluded dynamically — playlist changes take effect immediately without a server restart. |
| **Non-destructive** | The plugin filters the HTTP response; it never modifies watch-history, marks episodes as played, or changes library data. |

---

## Requirements

- Jellyfin **10.9.x** (targets `net8.0`)
- No external dependencies beyond the standard Jellyfin NuGet packages.

---

## Building

```bash
dotnet build -c Release
```

The compiled DLL is in `bin/Release/net8.0/`.

### Installing

1. Copy `Jellyfin.Plugin.NextUpFilter.dll` into your Jellyfin plugin folder, e.g.:
   - Linux: `/var/lib/jellyfin/plugins/NextUpFilter/`
   - Docker: the folder mapped to `/config/plugins/`
2. Restart Jellyfin.
3. Open **Dashboard → Plugins → Next Up Filter** to configure.

---

## Configuration

### Excluded series

Use the search box to find a series by name; click it to add it to the chip list.
Click **×** on a chip to remove it.

### Exclusion playlist

Create a regular Jellyfin playlist containing any mix of series, seasons, or episodes you want excluded. Select that playlist in the dropdown.
The plugin resolves each item's parent series at request time, so adding/removing items from the playlist is immediately reflected in "Next Up".

---

## How it works

The plugin registers an ASP.NET Core middleware (via `IStartupFilter`) that runs before Jellyfin's routing. When a `GET /Shows/NextUp` request arrives:

1. The upstream handler produces the normal Next Up JSON.
2. The middleware reads the response body.
3. Each episode is checked against the combined exclusion set (manual list + playlist-derived IDs).
4. Excluded episodes are removed; `TotalRecordCount` is updated accordingly.
5. The filtered JSON is returned to the client.

Pagination note: filtering happens after Jellyfin applies its own `Limit`/`StartIndex` parameters. A page that would normally show 20 items may therefore return fewer. This is a known trade-off of response-side filtering.

---

## Project structure

```
Jellyfin.Plugin.NextUpFilter/
├── Plugin.cs                        # IHasWebPages + BasePlugin<T>
├── PluginConfiguration.cs           # Persisted settings model
├── PluginServiceRegistrator.cs      # Registers IStartupFilter
├── Middleware/
│   ├── NextUpFilterStartupFilter.cs # Prepends middleware to pipeline
│   └── NextUpFilterMiddleware.cs    # Core filtering logic
└── Configuration/
    └── configPage.html              # Embedded admin UI
```

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.WebSearch;

namespace Yottacast.Core.Services;

/// <summary>
/// Central watcher for the plugins directory (AppPaths.PluginsDir).
/// Monitors all *.json files and fires PluginsChanged on any addition, removal or modification.
/// Also loads WebSearch plugins (websearch.*.json) and caches their icons.
/// </summary>
public class PluginService(ILogger<PluginService> logger) : IDisposable {
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private FileSystemWatcher? _watcher;

    public IReadOnlyList<WebSearchPlugin> Plugins { get; private set; } = [];

    /// <summary>Fired on the thread pool when any *.json file in the plugins directory changes.</summary>
    public event Action? PluginsChanged;

    // In-memory icon cache: pluginId → PNG/ICO bytes (null if unavailable)
    private readonly Dictionary<string, byte[]?> _icons = new();

    public byte[]? GetIcon(string id) => _icons.GetValueOrDefault(id);

    public async Task StartAsync() {
        Directory.CreateDirectory(AppPaths.PluginsDir);
        Directory.CreateDirectory(AppPaths.PluginIconCacheDir);
        await ReloadAsync();
        SetupWatcher();
    }

    private void SetupWatcher() {
        _watcher = new FileSystemWatcher(AppPaths.PluginsDir) {
            Filter = "*.json",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnDirectoryChanged;
        _watcher.Changed += OnDirectoryChanged;
        _watcher.Deleted += OnDirectoryChanged;
        _watcher.Renamed += OnDirectoryChanged;
    }

    private async void OnDirectoryChanged(object _, FileSystemEventArgs __) {
        await Task.Delay(300);  // small debounce: editors often write in multiple steps
        await ReloadAsync();
    }

    private async Task ReloadAsync() {
        var plugins = new List<WebSearchPlugin>();

        foreach (var file in Directory.EnumerateFiles(AppPaths.PluginsDir, "websearch.*.json")) {
            try {
                var json = await File.ReadAllTextAsync(file);
                var raw = JsonSerializer.Deserialize<PluginFileData>(json, JsonOptions);
                if (raw is null) continue;
                if (string.IsNullOrWhiteSpace(raw.Id) || string.IsNullOrWhiteSpace(raw.Name) || string.IsNullOrWhiteSpace(raw.QueryUrl)) {
                    logger.LogWarning("Plugin {File}: missing required fields (id, name, queryUrl), skipping", Path.GetFileName(file));
                    continue;
                }

                plugins.Add(new WebSearchPlugin {
                    Id                 = raw.Id,
                    Name               = raw.Name,
                    QueryUrl           = raw.QueryUrl,
                    IconUrl            = raw.IconUrl,
                    DefaultPrefix      = raw.DefaultPrefix ?? "",
                    ShowAlwaysPattern  = raw.ShowAlwaysPattern,
                    SourceFilePath     = file,
                });
            } catch (Exception ex) {
                logger.LogWarning("Plugin {File}: failed to parse ({Message}), skipping", Path.GetFileName(file), ex.Message);
            }
        }

        // Download missing icons for all plugins
        foreach (var plugin in plugins) {
            if (string.IsNullOrEmpty(plugin.IconUrl)) continue;
            var cachePath = IconCachePath(plugin.Id);
            if (!File.Exists(cachePath))
                await TryDownloadIconAsync(plugin.Id, plugin.IconUrl, cachePath);
        }

        // Load all icon bytes into memory
        var newIcons = new Dictionary<string, byte[]?>();
        foreach (var plugin in plugins) {
            var cachePath = IconCachePath(plugin.Id);
            newIcons[plugin.Id] = File.Exists(cachePath) ? await TryReadBytesAsync(cachePath) : null;
        }

        lock (_icons) {
            _icons.Clear();
            foreach (var kv in newIcons)
                _icons[kv.Key] = kv.Value;
        }

        Plugins = plugins;
        logger.LogDebug("Plugins reloaded: {Count} WebSearch plugin(s)", plugins.Count);
        PluginsChanged?.Invoke();
    }

    private async Task TryDownloadIconAsync(string id, string iconUrl, string cachePath) {
        try {
            var bytes = await _http.GetByteArrayAsync(iconUrl);
            await File.WriteAllBytesAsync(cachePath, bytes);
            logger.LogDebug("Plugin icon downloaded: {Id} from {Url}", id, iconUrl);
        } catch (Exception ex) {
            logger.LogDebug("Plugin icon download failed for {Id} ({Url}): {Message}", id, iconUrl, ex.Message);
        }
    }

    private static async Task<byte[]?> TryReadBytesAsync(string path) {
        try {
            return await File.ReadAllBytesAsync(path);
        } catch {
            return null;
        }
    }

    private static string IconCachePath(string id) =>
        Path.Combine(AppPaths.PluginIconCacheDir, id + ".ico");

    public void Dispose() {
        _watcher?.Dispose();
        _http.Dispose();
    }

    // ── JSON deserialization model ────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record PluginFileData {
        [JsonPropertyName("id")]                 public string? Id                { get; init; }
        [JsonPropertyName("name")]               public string? Name              { get; init; }
        [JsonPropertyName("queryUrl")]           public string? QueryUrl          { get; init; }
        [JsonPropertyName("iconUrl")]            public string? IconUrl           { get; init; }
        [JsonPropertyName("defaultPrefix")]      public string? DefaultPrefix     { get; init; }
        [JsonPropertyName("showAlwaysPattern")]  public string? ShowAlwaysPattern { get; init; }
    }
}

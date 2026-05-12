using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.WebSearch;

public class WebSearchSource(
    UserSettings settings,
    PluginService pluginService,
    BrowserDiscovery browserDiscovery,
    ILogger<WebSearchSource> logger) : IInstantSearchSource {

    private readonly Dictionary<string, byte[]?> _icons = LoadIcons();

    public int Limit => AppDefaults.WebSearchSourceLimit;

    public void Start() {
        pluginService.PluginsChanged += OnPluginsChanged;
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop() {
        pluginService.PluginsChanged -= OnPluginsChanged;
        return Task.CompletedTask;
    }

    private void OnPluginsChanged() => settings.EnsurePluginSettings(pluginService.Plugins);

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!settings.EnableWebSearch) return [];
        if (string.IsNullOrWhiteSpace(query) || query.StartsWith(':'))
            return [];

        var results = new List<BaseResultItemViewModel>();

        // Check if any engine (built-in or plugin) has a prefix match,
        // so ShowAlways engines can be suppressed when a prefix is active.
        var anyPrefixMatch = HasAnyPrefixMatch(query);

        // Built-in engines
        foreach (var engine in WebSearchDefaults.Engines) {
            var userConfig = settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                             ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);
            var result = TryBuildResult(engine.Id, engine.Name, engine.QueryUrl,
                _icons.GetValueOrDefault(engine.Id), userConfig, query, anyPrefixMatch);
            if (result != null) results.Add(result);
        }

        // Plugin engines
        foreach (var plugin in pluginService.Plugins) {
            var userConfig = settings.WebSearchEngines.FirstOrDefault(s => s.Id == plugin.Id);
            if (userConfig == null) continue;
            var result = TryBuildResult(plugin.Id, plugin.Name, plugin.QueryUrl,
                pluginService.GetIcon(plugin.Id), userConfig, query, anyPrefixMatch);
            if (result != null) results.Add(result);
        }

        logger.LogDebug("WebSearch query=\"{Query}\" results={Count}", query, results.Count);
        return results;
    }

    private bool HasAnyPrefixMatch(string query) {
        foreach (var engine in WebSearchDefaults.Engines) {
            var cfg = settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                      ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);
            if (IsPrefixMatch(cfg, query)) return true;
        }
        foreach (var plugin in pluginService.Plugins) {
            var cfg = settings.WebSearchEngines.FirstOrDefault(s => s.Id == plugin.Id);
            if (cfg != null && IsPrefixMatch(cfg, query)) return true;
        }
        return false;
    }

    private static bool IsPrefixMatch(WebSearchEngineSettings cfg, string query) {
        if (!cfg.Enabled || cfg.Mode != WebSearchMode.PrefixOnly) return false;
        var trigger = cfg.Prefix + " ";
        return !string.IsNullOrEmpty(cfg.Prefix)
               && query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)
               && query.Length > trigger.Length;
    }

    private ResultItemViewModel? TryBuildResult(string id, string name, string defaultQueryUrl,
        byte[]? iconBytes, WebSearchEngineSettings userConfig, string query, bool anyPrefixMatch) {

        if (!userConfig.Enabled) return null;

        string searchQuery;
        double score;

        string scoreReason;
        if (userConfig.Mode == WebSearchMode.ShowAlways) {
            if (anyPrefixMatch) return null;
            searchQuery = query;
            score = 0.4;
            scoreReason = "Búsqueda web siempre";
        } else {
            var prefix = userConfig.Prefix;
            if (string.IsNullOrEmpty(prefix)) return null;
            var trigger = prefix + " ";
            if (!query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) return null;
            searchQuery = query[trigger.Length..].Trim();
            if (string.IsNullOrEmpty(searchQuery)) return null;
            score = 3.8;
            scoreReason = "Búsqueda web prefijo";
        }

        var capturedQuery = searchQuery;
        var capturedQueryUrl = string.IsNullOrEmpty(userConfig.QueryUrl) ? defaultQueryUrl : userConfig.QueryUrl;
        var browserName = settings.ActiveBrowser?.Name ?? "browser";
        return new ResultItemViewModel {
            IconBytes   = iconBytes,
            Title       = $"{name}: \"{capturedQuery}\"",
            Subtitle    = $"Open search in {browserName}",
            Category    = "Web",
            Score       = score,
            ScoreReason = scoreReason,
            Actions = [
                new() {
                    Label        = "Open in browser",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute = () => {
                        var browser = settings.ActiveBrowser;
                        if (browser is null) return;
                        var url = string.Format(capturedQueryUrl, Uri.EscapeDataString(capturedQuery));
                        logger.LogInformation("WebSearch: open engine={Engine} query=\"{Query}\" browser={Browser}", name, capturedQuery, browser.Name);
                        browserDiscovery.OpenUrl(url, browser);
                    },
                },
            ],
        };
    }

    private static Dictionary<string, byte[]?> LoadIcons() {
        var dict = new Dictionary<string, byte[]?>();
        foreach (var engine in WebSearchDefaults.Engines)
            dict[engine.Id] = LoadIcon(engine.IconResource);
        return dict;
    }

    private static byte[]? LoadIcon(string? resourceName) {
        if (resourceName is null) return null;
        var stream = typeof(WebSearchSource).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

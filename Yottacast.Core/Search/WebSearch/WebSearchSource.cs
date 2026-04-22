using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.WebSearch;

public class WebSearchSource(
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    ILogger<WebSearchSource> logger) : IInstantSearchSource {

    private readonly Dictionary<string, byte[]?> _icons = LoadIcons();

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (string.IsNullOrWhiteSpace(query) || query.StartsWith(':'))
            return [];

        var results = new List<BaseResultItemViewModel>();

        var anyPrefixMatch = WebSearchDefaults.Engines.Any(engine => {
            var cfg = settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                      ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);
            if (!cfg.Enabled || cfg.Mode != WebSearchMode.PrefixOnly) return false;
            var trigger = cfg.Prefix + " ";
            return !string.IsNullOrEmpty(cfg.Prefix)
                   && query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)
                   && query.Length > trigger.Length;
        });

        foreach (var engine in WebSearchDefaults.Engines) {
            var userConfig = settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                             ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);

            if (!userConfig.Enabled) continue;

            string searchQuery;
            double score;

            if (userConfig.Mode == WebSearchMode.ShowAlways) {
                if (anyPrefixMatch) continue;
                searchQuery = query;
                score = 3.0;
            } else {
                // PrefixOnly: only activate when query is "{prefix} {text}"
                var prefix = userConfig.Prefix;
                if (string.IsNullOrEmpty(prefix)) continue;
                var trigger = prefix + " ";
                if (!query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) continue;
                searchQuery = query[trigger.Length..].Trim();
                if (string.IsNullOrEmpty(searchQuery)) continue;
                score = 3.5;  // explicit intent → higher than ShowAlways
            }

            var capturedQuery = searchQuery;
            var capturedEngine = engine;
            var capturedQueryUrl = string.IsNullOrEmpty(userConfig.QueryUrl) ? engine.QueryUrl : userConfig.QueryUrl;
            results.Add(new ResultItemViewModel {
                IconBytes  = _icons.GetValueOrDefault(engine.Id),
                Title      = $"{capturedEngine.Name}: {capturedQuery}",
                Subtitle   = "Open in browser",
                Category   = "Web",
                Score      = score,
                BypassLimit = true,
                OnActivate = () => {
                    var browser = settings.ActiveBrowser;
                    if (browser is null) return;
                    var url = string.Format(capturedQueryUrl, Uri.EscapeDataString(capturedQuery));
                    browserDiscovery.OpenUrl(url, browser);
                },
            });
        }

        logger.LogDebug("WebSearch query=\"{Query}\" results={Count}", query, results.Count);
        return results;
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

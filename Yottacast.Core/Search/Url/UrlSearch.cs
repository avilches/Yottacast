using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Url;

public class UrlSearch(
    HttpClient httpClient,
    UserSettings settings,
    BrowserDiscovery browserDiscovery,
    AppIconCache appIconCache,
    ILogger<UrlSearch> logger) : IInstantSearchSource {

    private enum UrlReachability { Pending, Valid, Invalid }

    private readonly ConcurrentDictionary<string, UrlReachability> _reachability = new();
    private readonly ConcurrentDictionary<string, byte[]?> _favicons = new();

    /// <summary>Fires (on a thread-pool thread) when reachability or favicon state changes.</summary>
    public event Action? ResultChanged;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() {
        _reachability.Clear();
        _favicons.Clear();
        return Task.CompletedTask;
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!TryNormalizeUrl(query, out var url)) return [];

        var reachability = _reachability.GetOrAdd(url, key => {
            _ = CheckReachabilityAsync(key);
            return UrlReachability.Pending;
        });

        if (reachability == UrlReachability.Invalid) return [];

        var browser = settings.ActiveBrowser;
        var subtitle = browser is null ? "Open in browser" : $"Open in {browser.Name}";
        var capturedUrl = url;

        byte[]? iconBytes = _favicons.GetValueOrDefault(url)
                            ?? (browser is null ? null : appIconCache.Get(browser.ExecutablePath));

        return [new ResultItemViewModel {
            IconBytes   = iconBytes,
            Title       = url.Length > 80 ? url[..77] + "…" : url,
            Subtitle    = subtitle,
            Category    = "Web",
            Score       = 3.0,
            BypassLimit = true,
            OnActivate  = () => {
                if (browser is null) {
                    logger.LogWarning("UrlSearch: cannot open \"{Url}\" — no browser configured", capturedUrl);
                    return;
                }
                logger.LogInformation("UrlSearch: open \"{Url}\" in {Browser}", capturedUrl, browser.Name);
                browserDiscovery.OpenUrl(capturedUrl, browser);
            },
        }];
    }

    private async Task CheckReachabilityAsync(string url) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            // Any HTTP response means the server is alive — mark Valid even for 403/405
            // (many sites block HEAD but are perfectly reachable in the browser).
            // Only network-level failures (exceptions) mark the URL as Invalid.
            var status = (int)response.StatusCode;
            _reachability[url] = UrlReachability.Valid;
            logger.LogDebug("UrlSearch: HEAD {Url} → {Status}", url, status);
            ResultChanged?.Invoke();
            _ = LoadFaviconAsync(url);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) {
            _reachability[url] = UrlReachability.Invalid;
            logger.LogDebug("UrlSearch: HEAD {Url} failed: {Message}", url, ex.Message);
            ResultChanged?.Invoke();
        }
    }

    private async Task LoadFaviconAsync(string url) {
        try {
            var host = new Uri(url).Host;
            var faviconUrl = $"https://www.google.com/s2/favicons?sz=64&domain={host}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var bytes = await httpClient.GetByteArrayAsync(faviconUrl, cts.Token).ConfigureAwait(false);
            _favicons[url] = bytes.Length > 0 ? bytes : null;
            if (bytes.Length > 0) {
                logger.LogDebug("UrlSearch: favicon loaded for {Url} ({N} bytes)", url, bytes.Length);
                ResultChanged?.Invoke();
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) {
            _favicons[url] = null;
            logger.LogDebug("UrlSearch: favicon failed for {Url}: {Message}", url, ex.Message);
        }
    }

    private static readonly HashSet<string> KnownTlds = new(StringComparer.OrdinalIgnoreCase) {
        "com", "net", "org", "io", "co", "uk", "de", "es", "fr", "dev", "app", "ai", "edu", "gov"
    };

    /// <summary>
    /// Returns true and sets <paramref name="url"/> to the normalized https:// URL
    /// if <paramref name="query"/> looks like a URL.
    /// </summary>
    public static bool TryNormalizeUrl(string query, out string url) {
        url = "";
        if (string.IsNullOrWhiteSpace(query) || query.Contains(' ')) return false;

        if (query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            url = query;
            return true;
        }

        if (query.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) {
            url = "https://" + query;
            return true;
        }

        if (LooksLikeDomain(query)) {
            url = "https://" + query;
            return true;
        }

        return false;
    }

    private static bool LooksLikeDomain(string query) {
        if (query.Length < 4) return false;
        var slashIdx = query.IndexOf('/');
        var host = slashIdx >= 0 ? query[..slashIdx] : query;
        var dotIdx = host.LastIndexOf('.');
        if (dotIdx <= 0 || dotIdx >= host.Length - 1) return false;
        var tld = host[(dotIdx + 1)..];
        return KnownTlds.Contains(tld);
    }
}

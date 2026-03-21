using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;

namespace Yottacast.Core.Services;

public record BrowserInfo(string Name, string ExecutablePath);

public class BrowserDiscovery(ApplicationSearch appSearch, PlatformProvider platform) {
    /// <summary>
    /// Returns all known browsers from the application cache (installed ones first,
    /// then falls back to the primary search-path candidate for settings pickers).
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetCandidatePaths() =>
        platform.KnownBrowserNames
            .Select(name => {
                var app = appSearch.Find(name);
                if (app is not null) return (name, app.Path);
                var paths = platform.GetBrowserPaths(name);
                return (name, paths.Length > 0 ? paths[0] : "");
            })
            .Where(t => !string.IsNullOrEmpty(t.Item2))
            .ToList();

    /// <summary>
    /// Returns only the browsers that are actually installed, using the application cache.
    /// </summary>
    public IReadOnlyList<BrowserInfo> Discover() =>
        platform.KnownBrowserNames
            .Select(name => {
                var app = appSearch.Find(name);
                if (app is not null) return new BrowserInfo(app.Name, app.Path);
                var paths = platform.BrowserFallbackPaths.TryGetValue(name, out var fp) ? fp : [];
                var existing = paths.FirstOrDefault(File.Exists);
                return existing is not null ? new BrowserInfo(name, existing) : null;
            })
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

    public Task<IReadOnlyList<BrowserInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());

    /// <summary>
    /// Opens the given URL in the specified browser via the platform launcher.
    /// </summary>
    public void OpenUrl(string url, BrowserInfo browser) =>
        platform.OpenUrl(url, browser.Name);

    /// <summary>
    /// Returns the preferred browser if it exists on disk, otherwise the first known browser found on disk.
    /// Does not require the ApplicationSearch cache to be populated.
    /// </summary>
    public static BrowserInfo? Resolve(string preferredName, PlatformProvider platform) {
        if (!string.IsNullOrEmpty(preferredName)) {
            var paths = platform.GetBrowserPaths(preferredName);
            var existing = paths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
            if (existing is not null) return new BrowserInfo(preferredName, existing);
        }
        foreach (var name in platform.KnownBrowserNames) {
            var paths = platform.GetBrowserPaths(name);
            var existing = paths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
            if (existing is not null) return new BrowserInfo(name, existing);
        }
        return null;
    }
}

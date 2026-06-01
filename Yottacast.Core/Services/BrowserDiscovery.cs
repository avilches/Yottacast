using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

public record BrowserInfo(string Name, string ExecutablePath);

public class BrowserDiscovery(UserSettings settings, PlatformProvider platform, ILogger<BrowserDiscovery> logger) {
    private IReadOnlyList<BrowserInfo>? _cache;

    /// <summary>
    /// Returns only the browsers that are actually installed, checking user-configured
    /// app directories, platform defaults, and platform-specific known paths on disk.
    /// Results are cached until <see cref="InvalidateCache"/> is called.
    /// </summary>
    public IReadOnlyList<BrowserInfo> Discover() {
        if (_cache is not null) return _cache;
        _cache = platform.KnownBrowserNames
            .Select(name => FindBrowser(name, platform, settings.ExpandedAppDirectories))
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();
        logger.LogInformation("BrowserDiscovery: cache loaded {Count} browsers: {Names}",
            _cache.Count, string.Join(", ", _cache.Select(b => b.Name)));
        return _cache;
    }

    public Task<IReadOnlyList<BrowserInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());

    /// <summary>Clears the cached discovery results so the next Discover() re-scans disk.</summary>
    public void InvalidateCache() {
        _cache = null;
        logger.LogInformation("BrowserDiscovery: cache invalidated");
    }

    /// <summary>
    /// Opens the given URL in the specified browser via the platform launcher.
    /// </summary>
    public void OpenUrl(string url, BrowserInfo browser) =>
        platform.OpenUrl(url, browser.Name);

    public void OpenUrlInBackground(string url, BrowserInfo browser) =>
        platform.OpenUrlInBackground(url, browser.Name);

    /// <summary>
    /// Returns the preferred browser if it exists on disk, otherwise the first known browser found on disk.
    /// Checks user-configured app directories, platform defaults, and platform-specific known paths.
    /// </summary>
    public static BrowserInfo? Resolve(string preferredName, PlatformProvider platform, IReadOnlyList<string> appDirectories) {
        if (!string.IsNullOrEmpty(preferredName)) {
            var found = FindBrowser(preferredName, platform, appDirectories);
            if (found is not null) return found;
        }
        foreach (var name in platform.KnownBrowserNames) {
            var found = FindBrowser(name, platform, appDirectories);
            if (found is not null) return found;
        }
        return null;
    }

    private static BrowserInfo? FindBrowser(string name, PlatformProvider platform, IReadOnlyList<string> appDirectories) {
        var checkedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check user-configured app directories
        foreach (var dir in appDirectories) {
            if (!checkedDirs.Add(dir)) continue;
            var path = platform.AppPathInDirectory(dir, name);
            if (path is not null && (Directory.Exists(path) || File.Exists(path)))
                return new BrowserInfo(name, path);
        }

        // 2. Check platform default app directories
        foreach (var rawDir in platform.DefaultAppDirectories()) {
            var dir = PlatformProvider.ExpandPath(rawDir);
            if (!checkedDirs.Add(dir)) continue;
            var path = platform.AppPathInDirectory(dir, name);
            if (path is not null && (Directory.Exists(path) || File.Exists(path)))
                return new BrowserInfo(name, path);
        }

        // 3. Check platform-specific known paths (e.g. Windows hardcoded exe paths)
        var paths = platform.BrowserKnownPaths.TryGetValue(name, out var fp) ? fp : [];
        var existing = paths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
        if (existing is not null) return new BrowserInfo(name, existing);

        return null;
    }
}

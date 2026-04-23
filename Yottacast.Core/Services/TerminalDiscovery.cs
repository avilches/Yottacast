using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

public record TerminalInfo(string Name, string ExecutablePath);

public class TerminalDiscovery(UserSettings settings, PlatformProvider platform, ILogger<TerminalDiscovery> logger) {
    private IReadOnlyList<TerminalInfo>? _cache;

    /// <summary>
    /// Returns only the terminals that are actually installed, checking user-configured
    /// app directories, platform defaults, and platform-specific known paths on disk.
    /// Results are cached until <see cref="InvalidateCache"/> is called.
    /// </summary>
    public IReadOnlyList<TerminalInfo> Discover() {
        if (_cache is not null) return _cache;
        _cache = platform.KnownTerminalNames
            .Select(name => FindTerminal(name, platform, settings.ExpandedAppDirectories))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
        logger.LogInformation("TerminalDiscovery: cache loaded {Count} terminals: {Names}",
            _cache.Count, string.Join(", ", _cache.Select(t => t.Name)));
        return _cache;
    }

    public Task<IReadOnlyList<TerminalInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());

    /// <summary>Clears the cached discovery results so the next Discover() re-scans disk.</summary>
    public void InvalidateCache() {
        _cache = null;
        logger.LogInformation("TerminalDiscovery: cache invalidated");
    }

    /// <summary>
    /// Executes the given command in the specified terminal via the platform launcher.
    /// </summary>
    public void ExecuteCommand(string command, TerminalInfo terminal) =>
        platform.ExecuteCommand(command, terminal.Name);

    /// <summary>
    /// Returns the preferred terminal if it exists on disk, otherwise the first known terminal found on disk.
    /// Checks user-configured app directories, platform defaults, and platform-specific known paths.
    /// </summary>
    public static TerminalInfo? Resolve(string preferredName, PlatformProvider platform, IReadOnlyList<string> appDirectories) {
        if (!string.IsNullOrEmpty(preferredName)) {
            var found = FindTerminal(preferredName, platform, appDirectories);
            if (found is not null) return found;
        }
        foreach (var name in platform.KnownTerminalNames) {
            var found = FindTerminal(name, platform, appDirectories);
            if (found is not null) return found;
        }
        return null;
    }

    private static TerminalInfo? FindTerminal(string name, PlatformProvider platform, IReadOnlyList<string> appDirectories) {
        var checkedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check user-configured app directories
        foreach (var dir in appDirectories) {
            if (!checkedDirs.Add(dir)) continue;
            var path = platform.AppPathInDirectory(dir, name);
            if (path is not null && (Directory.Exists(path) || File.Exists(path)))
                return new TerminalInfo(name, path);
        }

        // 2. Check platform default app directories
        foreach (var rawDir in platform.DefaultAppDirectories()) {
            var dir = PlatformProvider.ExpandPath(rawDir);
            if (!checkedDirs.Add(dir)) continue;
            var path = platform.AppPathInDirectory(dir, name);
            if (path is not null && (Directory.Exists(path) || File.Exists(path)))
                return new TerminalInfo(name, path);
        }

        // 3. Check platform-specific known paths (e.g. Windows hardcoded exe paths)
        var paths = platform.TerminalKnownPaths.TryGetValue(name, out var fp) ? fp : [];
        var existing = paths.FirstOrDefault(p => !p.Contains('*') && (Directory.Exists(p) || File.Exists(p)));
        if (existing is not null) return new TerminalInfo(name, existing);

        return null;
    }
}

using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;

namespace Yottacast.Core.Services;

public record TerminalInfo(string Name, string ExecutablePath);

public class TerminalDiscovery(ApplicationSearch appSearch, PlatformProvider platform, ILogger<TerminalDiscovery> logger) {
    /// <summary>
    /// Returns all known terminals from the application cache (installed ones first,
    /// then falls back to the primary search-path candidate for settings pickers).
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetCandidatePaths() =>
        platform.KnownTerminalNames
            .Select(name => {
                var app = appSearch.Find(name);
                if (app is not null) return (name, app.Path);
                var paths = platform.GetTerminalPaths(name);
                return (name, paths.Length > 0 ? paths[0] : "");
            })
            .Where(t => !string.IsNullOrEmpty(t.Item2))
            .ToList();

    /// <summary>
    /// Returns only the terminals that are actually installed, using the application cache.
    /// </summary>
    public IReadOnlyList<TerminalInfo> Discover() =>
        platform.KnownTerminalNames
            .Select(name => {
                var app = appSearch.Find(name);
                if (app is not null) return new TerminalInfo(app.Name, app.Path);
                var paths = platform.TerminalFallbackPaths.TryGetValue(name, out var fp) ? fp : [];
                var existing = paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p));
                return existing is not null ? new TerminalInfo(name, existing) : null;
            })
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

    public Task<IReadOnlyList<TerminalInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());

    /// <summary>
    /// Executes the given command in the specified terminal via the platform launcher.
    /// </summary>
    public void ExecuteCommand(string command, TerminalInfo terminal) =>
        platform.ExecuteCommand(command, terminal.Name);

    /// <summary>
    /// Returns the preferred terminal if it exists on disk, otherwise the first known terminal found on disk.
    /// Does not require the ApplicationSearch cache to be populated.
    /// </summary>
    public static TerminalInfo? Resolve(string preferredName, PlatformProvider platform) {
        if (!string.IsNullOrEmpty(preferredName)) {
            var paths = platform.GetTerminalPaths(preferredName);
            var existing = paths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
            if (existing is not null) return new TerminalInfo(preferredName, existing);
        }
        foreach (var name in platform.KnownTerminalNames) {
            var paths = platform.GetTerminalPaths(name);
            var existing = paths.FirstOrDefault(p => Directory.Exists(p) || File.Exists(p));
            if (existing is not null) return new TerminalInfo(name, existing);
        }
        return null;
    }
}

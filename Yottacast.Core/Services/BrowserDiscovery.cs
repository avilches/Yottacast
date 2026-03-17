using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.Storage;

namespace Yottacast.Core.Services;

public record BrowserInfo(string Name, string ExecutablePath);

public class BrowserDiscovery(ApplicationStorage appStorage) {
    private static readonly string[] KnownMacBrowsers = [
        "Safari",
        "Google Chrome",
        "Firefox",
        "Brave Browser",
        "Microsoft Edge",
        "Opera",
        "Arc",
        "Vivaldi",
        "Chromium",
        "Tor Browser",
        "DuckDuckGo",
        "Orion",
    ];

    private static string[] MacSearchPaths => [
        "/Applications",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
    ];

    private static readonly (string Name, string[] Paths)[] KnownWindowsBrowsers = [
        ("Google Chrome",   [@"C:\Program Files\Google\Chrome\Application\chrome.exe",
                             @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"]),
        ("Mozilla Firefox", [@"C:\Program Files\Mozilla Firefox\firefox.exe",
                             @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"]),
        ("Microsoft Edge",  [@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"]),
        ("Brave Browser",   [@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"]),
        ("Opera",           [@"C:\Program Files\Opera\opera.exe"]),
        ("Vivaldi",         [@"C:\Program Files\Vivaldi\Application\vivaldi.exe"]),
    ];

    /// <summary>
    /// Returns all known browsers from the application cache (installed ones first,
    /// then falls back to the primary search-path candidate for settings pickers).
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetCandidatePaths() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            var searchPaths = MacSearchPaths;
            return KnownMacBrowsers
                .Select(name => {
                    var app = appStorage.Find(name);
                    if (app is not null) return (name, app.Path);
                    var primary = Path.Combine(searchPaths[0], $"{name}.app");
                    return (name, primary);
                })
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return KnownWindowsBrowsers
                .Select(c => {
                    var app = appStorage.Find(c.Name);
                    return (c.Name, app?.Path ?? c.Paths.FirstOrDefault(File.Exists) ?? c.Paths[0]);
                })
                .ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns only the browsers that are actually installed, using the application cache.
    /// </summary>
    public IReadOnlyList<BrowserInfo> Discover() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return KnownMacBrowsers
                .Select(n => appStorage.Find(n))
                .Where(a => a is not null)
                .Select(a => new BrowserInfo(a!.Name, a.Path))
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return KnownWindowsBrowsers
                .Select(c => {
                    var app = appStorage.Find(c.Name);
                    if (app is not null) return new BrowserInfo(app.Name, app.Path);
                    var path = c.Paths.FirstOrDefault(File.Exists);
                    return path is not null ? new BrowserInfo(c.Name, path) : null;
                })
                .Where(b => b is not null)
                .Select(b => b!)
                .ToList();
        }
        return [];
    }

    public Task<IReadOnlyList<BrowserInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());
}

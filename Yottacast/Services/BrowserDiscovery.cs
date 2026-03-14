using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Yottacast.Services;

public record BrowserInfo(string Name, string ExecutablePath);

public static class BrowserDiscovery {
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

    public static IReadOnlyList<(string Name, string Path)> GetCandidatePaths() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            var searchPaths = MacSearchPaths;
            return KnownMacBrowsers
                .Select(name => {
                    var found = searchPaths
                        .Select(b => Path.Combine(b, $"{name}.app"))
                        .FirstOrDefault(Directory.Exists);
                    var primary = Path.Combine(searchPaths[0], $"{name}.app");
                    return (name, found ?? primary);
                })
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return KnownWindowsBrowsers
                .Select(c => (c.Name, c.Paths.FirstOrDefault(File.Exists) ?? c.Paths[0]))
                .ToList();
        }
        return [];
    }

    public static IReadOnlyList<BrowserInfo> Discover() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return DiscoverMac();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return DiscoverWindows();
        }
        // TODO: missing Linux?
        return [];
    }

    private static List<BrowserInfo> DiscoverMac() {
        var results = new List<BrowserInfo>();

        foreach (var name in KnownMacBrowsers) {
            foreach (var basePath in MacSearchPaths) {
                var appPath = Path.Combine(basePath, $"{name}.app");
                if (!Directory.Exists(appPath)) continue;
                results.Add(new BrowserInfo(name, appPath));
                break;
            }
        }

        return results;
    }

    private static List<BrowserInfo> DiscoverWindows() {
        var results = new List<BrowserInfo>();

        foreach (var (name, paths) in KnownWindowsBrowsers) {
            var found = paths.FirstOrDefault(File.Exists);
            if (found is not null)
                results.Add(new BrowserInfo(name, found));
        }

        return results;
    }
}
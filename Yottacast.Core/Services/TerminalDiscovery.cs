using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.Search;

namespace Yottacast.Core.Services;

public record TerminalInfo(string Name, string ExecutablePath);

public class TerminalDiscovery {
    private static readonly string[] KnownMacTerminals = [
        "Terminal",
        "iTerm",
        "Warp",
        "Alacritty",
        "Kitty",
        "Hyper",
        "WezTerm",
        "Tabby",
    ];

    private static readonly (string Name, string[] Paths)[] KnownWindowsTerminals = [
        ("Windows Terminal", [@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe"]),
        ("PowerShell", [
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
        ]),
        ("Command Prompt", [@"C:\Windows\System32\cmd.exe"]),
        ("Git Bash", [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe"
        ]),
    ];

    private readonly ApplicationSearch _appSearch;

    public TerminalDiscovery(ApplicationSearch appSearch) {
        _appSearch = appSearch;
    }

    /// <summary>
    /// Returns all known terminals from the application cache (installed ones first,
    /// then falls back to the primary search-path candidate for settings pickers).
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetCandidatePaths() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            var searchPaths = new[] {
                "/Applications",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
                "/System/Applications/Utilities",
            };
            return KnownMacTerminals
                .Select(name => {
                    var app = _appSearch.Find(name);
                    if (app is not null) return (name, app.Path);
                    var primary = Path.Combine(searchPaths[0], $"{name}.app");
                    return (name, primary);
                })
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return KnownWindowsTerminals
                .Select(c => {
                    var app = _appSearch.Find(c.Name);
                    if (app is not null) return (c.Name, app.Path);
                    return (c.Name, c.Paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p)) ?? c.Paths[0]);
                })
                .ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns only the terminals that are actually installed, using the application cache.
    /// </summary>
    public IReadOnlyList<TerminalInfo> Discover() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return KnownMacTerminals
                .Select(n => _appSearch.Find(n))
                .Where(a => a is not null)
                .Select(a => new TerminalInfo(a!.Name, a.Path))
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return KnownWindowsTerminals
                .Select(c => {
                    var app = _appSearch.Find(c.Name);
                    if (app is not null) return new TerminalInfo(app.Name, app.Path);
                    var path = c.Paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p));
                    return path is not null ? new TerminalInfo(c.Name, path) : null;
                })
                .Where(t => t is not null)
                .Select(t => t!)
                .ToList();
        }
        return [];
    }

    public Task<IReadOnlyList<TerminalInfo>> DiscoverAsync(CancellationToken ct = default) =>
        Task.FromResult(Discover());

    /// <summary>
    /// Returns the preferred terminal if it exists on disk, otherwise the first known terminal found on disk.
    /// Does not require the ApplicationSearch cache to be populated.
    /// </summary>
    public static TerminalInfo? Resolve(string preferredName) {
        if (!string.IsNullOrEmpty(preferredName) && ExistsOnDisk(preferredName))
            return new TerminalInfo(preferredName, FindPath(preferredName));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return KnownMacTerminals
                .Where(ExistsOnDisk)
                .Select(n => new TerminalInfo(n, FindPath(n)))
                .FirstOrDefault();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return KnownWindowsTerminals
                .Select(c => { var p = c.Paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p)); return p is not null ? new TerminalInfo(c.Name, p) : null; })
                .FirstOrDefault(t => t is not null);
        return null;
    }

    private static bool ExistsOnDisk(string name) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Directory.Exists(Path.Combine("/Applications", $"{name}.app")) ||
                   Directory.Exists(Path.Combine(home, "Applications", $"{name}.app")) ||
                   Directory.Exists(Path.Combine("/System/Applications/Utilities", $"{name}.app"));
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var entry = KnownWindowsTerminals.FirstOrDefault(c => c.Name == name);
            return entry.Paths is not null && entry.Paths.Any(p => !p.Contains('*') && File.Exists(p));
        }
        return false;
    }

    private static string FindPath(string name) {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = new[] {
                Path.Combine("/Applications", $"{name}.app"),
                Path.Combine(home, "Applications", $"{name}.app"),
                Path.Combine("/System/Applications/Utilities", $"{name}.app"),
            };
            return paths.FirstOrDefault(Directory.Exists) ?? paths[0];
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var entry = KnownWindowsTerminals.FirstOrDefault(c => c.Name == name);
            return entry.Paths?.FirstOrDefault(p => !p.Contains('*') && File.Exists(p)) ?? "";
        }
        return "";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Yottacast.Services;

public record TerminalInfo(string Name, string ExecutablePath);

public static class TerminalDiscovery
{
    private static readonly string[] KnownMacTerminals =
    [
        "Terminal",
        "iTerm",
        "Warp",
        "Alacritty",
        "Kitty",
        "Hyper",
        "WezTerm",
        "Tabby",
    ];

    public static IReadOnlyList<(string Name, string Path)> GetCandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var searchPaths = new[]
            {
                "/Applications",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
                "/System/Applications/Utilities",
            };
            return KnownMacTerminals
                .Select(name => {
                    var found = searchPaths
                        .Select(b => Path.Combine(b, $"{name}.app"))
                        .FirstOrDefault(Directory.Exists);
                    var primary = Path.Combine(searchPaths[0], $"{name}.app");
                    return (name, found ?? primary);
                })
                .ToList();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new (string Name, string[] Paths)[]
            {
                ("Windows Terminal", [@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe"]),
                ("PowerShell",       [@"C:\Program Files\PowerShell\7\pwsh.exe",
                                      @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"]),
                ("Command Prompt",   [@"C:\Windows\System32\cmd.exe"]),
                ("Git Bash",         [@"C:\Program Files\Git\bin\bash.exe",
                                      @"C:\Program Files (x86)\Git\bin\bash.exe"]),
            }
            .Select(c => (c.Name, c.Paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p)) ?? c.Paths[0]))
            .ToList();
        }
        return [];
    }

    public static IReadOnlyList<TerminalInfo> Discover()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DiscoverMac();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DiscoverWindows();
        return [];
    }

    private static List<TerminalInfo> DiscoverMac()
    {
        var results = new List<TerminalInfo>();
        var searchPaths = new[]
        {
            "/Applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
            "/System/Applications/Utilities",
        };

        foreach (var name in KnownMacTerminals)
        {
            foreach (var basePath in searchPaths)
            {
                var appPath = Path.Combine(basePath, $"{name}.app");
                if (Directory.Exists(appPath))
                {
                    results.Add(new TerminalInfo(name, appPath));
                    break;
                }
            }
        }

        return results;
    }

    private static List<TerminalInfo> DiscoverWindows()
    {
        var results = new List<TerminalInfo>();
        var candidates = new (string Name, string[] Paths)[]
        {
            ("Windows Terminal", [@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal*\wt.exe"]),
            ("PowerShell",       [@"C:\Program Files\PowerShell\7\pwsh.exe",
                                  @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"]),
            ("Command Prompt",   [@"C:\Windows\System32\cmd.exe"]),
            ("Git Bash",         [@"C:\Program Files\Git\bin\bash.exe",
                                  @"C:\Program Files (x86)\Git\bin\bash.exe"]),
        };

        foreach (var (name, paths) in candidates)
        {
            var found = paths.FirstOrDefault(p => !p.Contains('*') && File.Exists(p));
            if (found is not null)
                results.Add(new TerminalInfo(name, found));
        }

        return results;
    }
}

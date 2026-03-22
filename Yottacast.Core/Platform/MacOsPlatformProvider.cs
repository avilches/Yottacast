using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class MacOsPlatformProvider(ILogger<MacOsPlatformProvider> logger) : PlatformProvider {
    // ── Dark mode ─────────────────────────────────────────────────────────────

    public override bool? IsSystemDarkMode() {
        try {
            using var p = System.Diagnostics.Process.Start(new ProcessStartInfo(
                "defaults", "read -g AppleInterfaceStyle") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && output.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        } catch {
            return null;
        }
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    public override List<string> DefaultAppDirectories() => ["/Applications", "$HOME/Applications", "/System/Applications"];

    public override List<string> DefaultSearchFolders() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            "$HOME/Downloads",
            "$HOME/Desktop",
            "$HOME/Documents",
            "$HOME/Movies",
            "$HOME/Pictures",
            "$HOME/Dropbox",
            "$HOME/Music",
            "$HOME/Public",
            "$HOME/Library/Mobile Documents/com~apple~CloudDocs",
            "$HOME/Library/Application Support",
            "$HOME/Library/Containers",
            "$HOME/Creative Cloud Files",
            "$HOME/Google Drive",
            "$HOME/OneDrive",
            "$HOME/Box Sync",
            "$HOME/Box",
            "$HOME/Mega",
            "$HOME/pCloud Drive",
            "$HOME/Nextcloud",
            "$HOME/Adobe Creative Cloud",
            "$HOME/Amazon Drive"
        ];
    }

    // ── App scanning ──────────────────────────────────────────────────────────

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        const string predicate = "kMDItemContentType == 'com.apple.application-bundle'";
        return Task.Run(() => SpotlightInterop.Query(
            predicate, dirs,
            line => { if (!string.IsNullOrWhiteSpace(line)) addApp(line); return true; },
            ct), ct);
    }

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs.Where(Directory.Exists)) {
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.app",
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => onAdded(e.FullPath);
            watcher.Deleted += (_, e) => onRemoved(e.FullPath);
            watchers.Add(watcher);
        }
        return watchers;
    }

    public override void LaunchApp(string path) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
        } catch { }
    }

    // ── File search ───────────────────────────────────────────────────────────

    public override async Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct) {
        var safeQuery = query.Replace("'", "\\'");
        if (string.IsNullOrEmpty(safeQuery)) return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var validFolders = folders?.Where(Directory.Exists).ToList();
        var invalidFolders = folders?.Where(f => !Directory.Exists(f)).ToList();
        if (invalidFolders?.Count > 0)
            logger.LogWarning("Spotlight skipping non-existent folders: [{Folders}]", string.Join(", ", invalidFolders));
        var scope = (validFolders?.Count > 0 ? validFolders : null) ?? [home];

        string predicate;
        if (safeQuery.Contains('*')) {
            predicate = $"kMDItemFSName == '{safeQuery}'cd";
        } else {
            var tokens = safeQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            predicate = string.Join(" && ", tokens.Select(t => $"kMDItemFSName == '*{t}*'cd"));
        }
        logger.LogDebug("Spotlight query: {Predicate} scope=[{Scope}]", predicate, string.Join(", ", scope));

        var count = 0;
        var sw = Stopwatch.StartNew();
        Exception? error = null;
        try {
            await Task.Run(() => SpotlightInterop.Query(
                predicate, scope,
                line => {
                    onResult(new FileResult(Path.GetFileName(line), line));
                    return ++count < maxResults;
                },
                ct), ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            error = ex;
        }
        sw.Stop();
        logger.LogDebug("Spotlight result: elapsed={ElapsedMs}ms cancelled={Cancelled} results={Count} error={Error}",
            sw.Elapsed.TotalMilliseconds, ct.IsCancellationRequested, count, error?.Message ?? "none");
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    public override string[] KnownBrowserNames => [
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

    public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths =>
        new Dictionary<string, string[]>();

    public override string[] GetBrowserPaths(string name) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            $"/Applications/{name}.app",
            $"$HOME/Applications/{name}.app",
        ];
    }

    public override void OpenUrl(string url, string browserName) {
        try {
            System.Diagnostics.Process.Start(new ProcessStartInfo {
                FileName = "open",
                ArgumentList = { "-a", browserName, url },
                UseShellExecute = false,
            });
        } catch { }
    }

    // ── Terminal ──────────────────────────────────────────────────────────────

    public override string[] KnownTerminalNames => [
        "Terminal",
        "iTerm",
        "Warp",
        "Alacritty",
        "Kitty",
        "Hyper",
        "WezTerm",
        "Tabby",
    ];

    public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths =>
        new Dictionary<string, string[]>();

    public override string[] GetTerminalPaths(string name) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            $"/Applications/{name}.app",
            $"$HOME/Applications/{name}.app",
            $"/System/Applications/Utilities/{name}.app",
        ];
    }

    public override void ExecuteCommand(string command, string terminalName) {
        switch (terminalName) {
            case "Terminal":
                RunAppleScript($"""tell application "Terminal" to do script "{EscapeAppleScript(command)}" """);
                break;
            case "iTerm":
                RunAppleScript($"""
                    tell application "iTerm"
                        create window with default profile command "{EscapeAppleScript(command)}"
                    end tell
                    """);
                break;
            case "Warp":
                var warpUrl = $"warp://action/new_tab?command={Uri.EscapeDataString(command)}";
                System.Diagnostics.Process.Start(new ProcessStartInfo {
                    FileName = "open",
                    ArgumentList = { warpUrl },
                    UseShellExecute = false,
                });
                break;
            default:
                var script = Path.GetTempFileName() + ".command";
                File.WriteAllText(script, $"#!/bin/sh\n{command}\n");
                System.Diagnostics.Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();
                System.Diagnostics.Process.Start(new ProcessStartInfo {
                    FileName = "open",
                    ArgumentList = { "-a", terminalName, script },
                    UseShellExecute = false,
                });
                break;
        }
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    public override string? GetAppIconPath(string appPath) {
        try {
            var plist = Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(plist)) return null;

            var content = File.ReadAllText(plist);
            var keyIdx = content.IndexOf("<key>CFBundleIconFile</key>", StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            var stringStart = content.IndexOf("<string>", keyIdx, StringComparison.Ordinal);
            if (stringStart < 0) return null;
            var stringEnd = content.IndexOf("</string>", stringStart + 8, StringComparison.Ordinal);
            if (stringEnd < 0) return null;

            var iconFile = content[(stringStart + 8)..stringEnd].Trim();
            if (!iconFile.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
                iconFile += ".icns";

            var iconPath = Path.Combine(appPath, "Contents", "Resources", iconFile);
            return File.Exists(iconPath) ? iconPath : null;
        } catch {
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RunAppleScript(string script) {
        System.Diagnostics.Process.Start(new ProcessStartInfo {
            FileName = "osascript",
            ArgumentList = { "-e", script },
            UseShellExecute = false,
        });
    }

    private static string EscapeAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

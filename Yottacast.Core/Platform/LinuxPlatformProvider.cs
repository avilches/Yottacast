using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class LinuxPlatformProvider(ProcessRunner runner, ILogger<LinuxPlatformProvider> logger) : PlatformProvider {

    // ── Dark mode ─────────────────────────────────────────────────────────────

    public override bool? IsSystemDarkMode() {
        try {
            using var p = System.Diagnostics.Process.Start(new ProcessStartInfo(
                "gsettings", "get org.gnome.desktop.interface color-scheme") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && output.Contains("prefer-dark", StringComparison.OrdinalIgnoreCase);
        } catch {
            return null;
        }
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    public override List<string> DefaultAppDirectories() => [
        "/usr/share/applications",
        "/usr/local/share/applications",
        "$HOME/.local/share/applications",
    ];

    public override List<string> DefaultSearchFolders() => [
        "$HOME/Downloads",
        "$HOME/Desktop",
        "$HOME/Documents",
        "$HOME/Videos",
        "$HOME/Pictures",
    ];

    // ── App scanning ──────────────────────────────────────────────────────────

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        foreach (var dir in dirs.Where(Directory.Exists))
            foreach (var desktop in Directory.EnumerateFiles(dir, "*.desktop"))
                addApp(desktop);
        return Task.CompletedTask;
    }

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs.Where(Directory.Exists)) {
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.desktop",
                NotifyFilter = NotifyFilters.FileName,
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
                new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = false });
        } catch { }
    }

    public override void RevealInFileManager(string directoryPath) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo("xdg-open", $"\"{directoryPath}\"") { UseShellExecute = false });
        } catch { }
    }

    public override void OpenFile(string filePath) {
        try {
            System.Diagnostics.Process.Start(
                new ProcessStartInfo("xdg-open", $"\"{filePath}\"") { UseShellExecute = false });
        } catch { }
    }

    // ── File search ───────────────────────────────────────────────────────────

    public override Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct) {
        var count = 0;
        Func<string, bool> onLine = line => {
            onResult(new FileResult(Path.GetFileName(line), line));
            return ++count < maxResults;
        };

        var binary = File.Exists("/usr/bin/plocate") ? "/usr/bin/plocate" : "/usr/bin/locate";
        var safeQuery = query.Replace("\"", "").Trim();
        if (string.IsNullOrEmpty(safeQuery)) return Task.CompletedTask;
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var tokens = safeQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return Task.CompletedTask;
        var primaryToken = tokens[0];
        var extraTokens = tokens.Skip(1).ToArray();

        Func<string, bool> filteredOnLine = line => {
            if (folders?.Count > 0 && !folders.Any(f => line.StartsWith(f, StringComparison.Ordinal)))
                return true;
            if (extraTokens.Length > 0 && extraTokens.Any(t => !Path.GetFileName(line).Contains(t, StringComparison.OrdinalIgnoreCase)))
                return true;
            return onLine(line);
        };

        return runner.RunAsync(binary,
            ["-b", "-l", maxResults.ToString(), $"*{primaryToken}*"],
            cwd, filteredOnLine, ct);
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    public override string[] KnownBrowserNames => [];
    public override void OpenUrl(string url, string browserName) { }

    // ── Terminal ──────────────────────────────────────────────────────────────

    public override string[] KnownTerminalNames => [];
    public override void ExecuteCommand(string command, string terminalName) { }

    // ── Icon ──────────────────────────────────────────────────────────────────

}

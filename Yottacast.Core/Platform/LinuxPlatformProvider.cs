using System.Diagnostics;
using Yottacast.Core.Process;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class LinuxPlatformProvider : PlatformProvider {
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

    public override List<string> DefaultAppDirectories() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            "/usr/share/applications",
            "/usr/local/share/applications",
            Path.Combine(home, ".local", "share", "applications"),
        ];
    }

    public override List<string> DefaultSearchFolders() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents"),
            Path.Combine(home, "Videos"),
            Path.Combine(home, "Pictures"),
        ];
    }

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

    // ── File search ───────────────────────────────────────────────────────────

    public override Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        RunnerBackend backend, IReadOnlyList<string>? folders, CancellationToken ct) {
        var count = 0;
        Func<string, bool> onLine = line => {
            onResult(new FileResult(Path.GetFileName(line), line));
            return ++count < maxResults;
        };

        var binary = File.Exists("/usr/bin/plocate") ? "/usr/bin/plocate" : "/usr/bin/locate";
        var safeQuery = query.Replace("\"", "");
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Func<string, bool> filteredOnLine = string.IsNullOrEmpty(folders?.FirstOrDefault())
            ? onLine
            : line => folders!.Any(f => line.StartsWith(f, StringComparison.Ordinal)) && onLine(line);

        return CommandRunner.RunAsync(backend, binary,
            ["-b", "-l", maxResults.ToString(), $"*{safeQuery}*"],
            cwd, filteredOnLine, ct);
    }

    // ── Browser ───────────────────────────────────────────────────────────────

    public override string[] KnownBrowserNames => [];
    public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths =>
        new Dictionary<string, string[]>();
    public override string[] GetBrowserPaths(string name) => [];
    public override void OpenUrl(string url, string browserName) { }

    // ── Terminal ──────────────────────────────────────────────────────────────

    public override string[] KnownTerminalNames => [];
    public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths =>
        new Dictionary<string, string[]>();
    public override string[] GetTerminalPaths(string name) => [];
    public override void ExecuteCommand(string command, string terminalName) { }

    // ── Icon ──────────────────────────────────────────────────────────────────

    public override string? GetAppIconPath(string appPath) => null;
}

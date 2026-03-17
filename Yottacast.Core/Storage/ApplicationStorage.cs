using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Yottacast.Core.Process;
using Yottacast.Core.Search;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Storage;

/// <summary>
/// Represents an installed application with a lazily-resolved icon path.
/// </summary>
public sealed class AppInfo {
    public string Name { get; }
    public string Path { get; }

    // Read Info.plist on first access — avoids parsing hundreds of files at startup
    private readonly Lazy<string?> _iconPath;
    public string? IconPath => _iconPath.Value;

    internal AppInfo(string name, string path) {
        Name = name;
        Path = path;
        _iconPath = new Lazy<string?>(
            () => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? TryGetMacIconPath(path) : null,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static string? TryGetMacIconPath(string appPath) {
        try {
            var plist = System.IO.Path.Combine(appPath, "Contents", "Info.plist");
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

            var iconPath = System.IO.Path.Combine(appPath, "Contents", "Resources", iconFile);
            return File.Exists(iconPath) ? iconPath : null;
        } catch {
            return null;
        }
    }
}

/// <summary>
/// In-memory cache of all installed applications. Implements <see cref="ISearchSource"/>
/// so it can be registered in <see cref="Search.SearchService"/>.
///
/// macOS  — subscribes to <see cref="FileSearch.SearchLiveAsync"/> (mdfind -live).
///          Receives the initial batch of apps and then live updates as apps are installed.
/// Windows/Linux — initial directory scan + FileSystemWatcher for live updates.
///
/// Call <see cref="Start"/> to begin scanning. Call <see cref="Stop"/> to cancel it.
/// BrowserDiscovery and TerminalDiscovery query this store instead of hitting the filesystem themselves.
/// </summary>
public sealed class ApplicationStorage : ISearchSource, IDisposable {
    private const string MacAppBundleQuery = "kMDItemContentType == 'com.apple.application-bundle'";

    private readonly ConcurrentDictionary<string, AppInfo> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    // macOS: task handle for the mdfind -live process (cancelled on Stop)
    private CancellationTokenSource _liveCts = new();

    // Windows/Linux: FileSystemWatchers
    private readonly List<FileSystemWatcher> _watchers = [];

    // ── ISearchSource ─────────────────────────────────────────────────────────

    public Task Start() {
        if (_started) return Task.CompletedTask;
        _started = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return StartMacAsync();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ScanWindows();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ScanLinux();
        return Task.CompletedTask;
    }

    public void Stop() {
        _started = false;
        _liveCts.Cancel();
        _liveCts.Dispose();
        _liveCts = new CancellationTokenSource();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    public Task<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, CancellationToken ct = default) {
        var results = _apps.Values
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(a => new ResultItemViewModel {
                Icon = "📱",
                Title = a.Name,
                Subtitle = a.Path,
                Category = "Applications",
                Score = 0,
                OnActivate = () => LaunchApp(a.Path),
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ResultItemViewModel>>(results);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public AppInfo? Find(string name) => _apps.GetValueOrDefault(name);

    public IReadOnlyList<AppInfo> FindAll() => [.. _apps.Values];

    public IReadOnlyList<AppInfo> Search(string query) =>
        _apps.Values
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // ── macOS — initial one-shot + background live ────────────────────────────

    private async Task StartMacAsync() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Func<string, bool> onLine = line => {
            if (!string.IsNullOrWhiteSpace(line)) AddApp(line);
            return true;
        };

        // 1. Initial batch: one-shot mdfind — awaitable, populates the store before returning.
        await StandardCommandRunner.Instance.RunAsync(
            "/usr/bin/mdfind", [MacAppBundleQuery], home, onLine, _liveCts.Token);

        // 2. Live updates in background — keeps the store in sync as apps are installed/removed.
        //    Duplicate adds are harmless: ConcurrentDictionary keyed by name just overwrites.
        _ = StandardCommandRunner.Instance.RunAsync(
            "/usr/bin/mdfind", ["-live", MacAppBundleQuery], home, onLine, _liveCts.Token);
    }

    // ── Windows — scan + FileSystemWatcher ───────────────────────────────────

    private void ScanWindows() {
        var dirs = new[] {
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };

        foreach (var dir in dirs.Where(Directory.Exists)) {
            foreach (var subDir in Directory.EnumerateDirectories(dir)) {
                // Prefer exe that matches the folder name (most common pattern), else first exe found
                var folderName = System.IO.Path.GetFileName(subDir);
                var exe = Directory.EnumerateFiles(subDir, $"{folderName}.exe").FirstOrDefault()
                       ?? Directory.EnumerateFiles(subDir, "*.exe").FirstOrDefault();
                if (exe is not null)
                    AddApp(exe);
            }

            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.exe",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => AddApp(e.FullPath);
            watcher.Deleted += (_, e) =>
                _apps.TryRemove(System.IO.Path.GetFileNameWithoutExtension(e.Name ?? ""), out AppInfo? _);
            _watchers.Add(watcher);
        }
    }

    // ── Linux — scan + FileSystemWatcher ─────────────────────────────────────

    private void ScanLinux() {
        var dirs = new[] {
            "/usr/share/applications",
            "/usr/local/share/applications",
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "applications"),
        };

        foreach (var dir in dirs.Where(Directory.Exists)) {
            foreach (var desktop in Directory.EnumerateFiles(dir, "*.desktop"))
                AddApp(desktop);

            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.desktop",
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => AddApp(e.FullPath);
            watcher.Deleted += (_, e) =>
                _apps.TryRemove(System.IO.Path.GetFileNameWithoutExtension(e.Name ?? ""), out AppInfo? _);
            _watchers.Add(watcher);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddApp(string path) {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return;
        _apps[name] = new AppInfo(name, path);
    }

    private static void LaunchApp(string path) {
        try {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                System.Diagnostics.Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
            else
                System.Diagnostics.Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } catch {
            // best-effort launch
        }
    }

    public void Dispose() => Stop();
}

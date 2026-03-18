using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Yottacast.Core.Process;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// In-memory cache of all installed applications. Implements <see cref="ISearchSource"/>
/// so it can be registered in <see cref="GlobalSearch"/>.
///
/// macOS  — one-shot mdfind via <see cref="StandardCommandRunner"/> for the initial batch,
///          then <see cref="FileSystemWatcher"/> on each AppDirectory for live *.app updates.
/// Windows/Linux — initial directory scan + FileSystemWatcher for live updates.
///
/// Call <see cref="Start"/> to begin scanning. Call <see cref="Stop"/> to cancel it.
/// Call <see cref="ReloadAppDirectories"/> to restart scanning after AppDirectories changes.
/// BrowserDiscovery and TerminalDiscovery query this store instead of hitting the filesystem themselves.
/// </summary>
public sealed class ApplicationSearch(UserSettings settings) : ISearchSource, IDisposable {
    private const string MacAppBundleQuery = "kMDItemContentType == 'com.apple.application-bundle'";

    private readonly ConcurrentDictionary<string, AppInfo> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    public event Action<AppInfo>? AppAdded;

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

    public Task Stop() {
        _started = false;
        _liveCts.Cancel();
        _liveCts.Dispose();
        _liveCts = new CancellationTokenSource();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _apps.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Restarts scanning with the current AppDirectories from UserSettings.
    /// Call this after the user changes AppDirectories in Settings.
    /// </summary>
    public async Task ReloadAppDirectories() {
        await Stop();
        await Start();
    }

    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var a in _apps.Values.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))) {
            ct.ThrowIfCancellationRequested();
            yield return new ResultItemViewModel {
                Icon = "📱",
                Title = a.Name,
                Subtitle = a.Path,
                Category = "Applications",
                Score = 1,
                OnActivate = () => LaunchApp(a.Path),
            };
        }
        await Task.CompletedTask; // async iterator requires at least one await
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

        // 1. Initial batch: one-shot mdfind — awaitable, populates the store before returning.
        await CommandRunner.RunAsync(RunnerBackend.Standard,
            "/usr/bin/mdfind", [MacAppBundleQuery], home,
            line => { if (!string.IsNullOrWhiteSpace(line)) AddApp(line); return true; },
            _liveCts.Token);

        // 2. Live updates via FileSystemWatcher on the configured app directories.
        //    mdfind -live only reports the match count, not which paths changed.
        foreach (var dir in settings.AppDirectories.Where(Directory.Exists)) {
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.app",
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => AddApp(e.FullPath);
            watcher.Deleted += (_, e) =>
                _apps.TryRemove(System.IO.Path.GetFileNameWithoutExtension(e.Name ?? ""), out AppInfo? _);
            _watchers.Add(watcher);
        }
    }

    // ── Windows — scan + FileSystemWatcher ───────────────────────────────────

    private void ScanWindows() {
        foreach (var dir in settings.AppDirectories.Where(Directory.Exists)) {
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
        foreach (var dir in settings.AppDirectories.Where(Directory.Exists)) {
            foreach (var desktop in Directory.EnumerateFiles(dir, "*.desktop"))
                AddApp(desktop);

            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.desktop",
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => AddApp(e.FullPath);
            watcher.Deleted += (_, e) =>
                _apps.TryRemove(Path.GetFileNameWithoutExtension(e.Name ?? ""), out AppInfo? _);
            _watchers.Add(watcher);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddApp(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return;
        var isNew = !_apps.ContainsKey(name);
        var app = new AppInfo(name, path);
        _apps[name] = app;
        if (isNew) AppAdded?.Invoke(app);
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

    public void Dispose() => Stop().GetAwaiter().GetResult();
}

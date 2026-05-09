using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Application;

/// <summary>
/// In-memory cache of all installed applications. Implements <see cref="IInstantSearchSource"/>
/// so it can be registered in <see cref="GlobalSearch"/>.
///
/// Startup is platform-specific: macOS runs mdfind one-shot then FileSystemWatcher;
/// Windows/Linux do a synchronous scan then FileSystemWatcher.
///
/// Call <see cref="Start"/> to begin scanning. Call <see cref="Stop"/> to cancel it.
/// When the user changes AppDirectories in Settings, <see cref="RescanAsync"/> re-scans
/// with a diff to avoid showing all apps as "new".
/// </summary>
public sealed class ApplicationSearch(
    UserSettings settings,
    PlatformProvider platform,
    AppIconCache iconCache,
    ClipboardService clipboard,
    ILogger<ApplicationSearch> logger)
    : IInstantSearchSource, IDisposable {
    private readonly ConcurrentDictionary<string, AppInfo> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    public event Action<AppInfo>? AppAdded;

    /// <summary>Fired when an app is installed, updated, or removed — subscribers can invalidate caches.</summary>
    public event Action? AppsChanged;

    /// <summary>Fired when any app icon finishes loading — subscribers should re-run Search to pick up the new icon.</summary>
    public event Action? IconLoaded {
        add    => iconCache.IconLoaded += value;
        remove => iconCache.IconLoaded -= value;
    }

    private CancellationTokenSource _liveCts = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private TaskCompletionSource _readyTcs = new();

    // ── IInstantSearchSource ──────────────────────────────────────────────────

    public int Limit => AppDefaults.AppSearchLimit;

    public void Start() {
        if (_started) return;
        _started = true;
        settings.AppDirectoriesChanged += OnAppDirectoriesChanged;
        if (!settings.EnableAppSearch) {
            _readyTcs.TrySetResult();
            return;
        }
        _ = ScanAndWatchAsync();
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _started = false;
        settings.AppDirectoriesChanged -= OnAppDirectoriesChanged;
        _liveCts.Cancel();
        _liveCts.Dispose();
        _liveCts = new CancellationTokenSource();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _apps.Clear();
        var oldTcs = _readyTcs;
        _readyTcs = new TaskCompletionSource();
        oldTcs.TrySetCanceled();
        return Task.CompletedTask;
    }

    public ResultItemViewModel CreateResultItem(AppInfo app, double score = 4.0) {
        var path = app.Path;
        return new() {
            Icon = "📱",
            IconBytes = iconCache.Get(path),
            Title = app.Name,
            Subtitle = path,
            ItemPath = path,
            Category = "Application",
            Score = score,
            Actions = [
                new() {
                    Label        = "Open",
                    Hotkey       = ActionHotkey.Enter,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    ClosesWindow = true,
                    Execute      = () => platform.LaunchApp(path),
                },
                new() {
                    Label        = "Copy path",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => "Path copied!",
                    Execute      = () => clipboard.CopyText(path),
                },
            ],
        };
    }

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableAppSearch) return [];
        var results = _apps.Values
            .Select(a => (app: a, score: NameMatcher.Score(a.Name, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => CreateResultItem(x.app, x.score * 4))
            .ToList();
        logger.LogDebug("AppSearch query=\"{Query}\" cache={CacheCount} results={ResultCount} ready={Ready}",
            query, _apps.Count, results.Count, _readyTcs.Task.IsCompleted);
        return results;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public AppInfo? Find(string name) => _apps.GetValueOrDefault(name);

    public IReadOnlyList<AppInfo> FindAll() => [.. _apps.Values];

    // ── Scan + watch ──────────────────────────────────────────────────────────

    private async Task ScanAndWatchAsync() {
        logger.LogInformation("AppSearch scan start dirs=[{Dirs}]", string.Join(", ", settings.ExpandedAppDirectories));
        await platform.ScanAppsAsync(AddApp, settings.ExpandedAppDirectories, _liveCts.Token);
        logger.LogInformation("AppSearch scan done apps={Count}", _apps.Count);
        _readyTcs.TrySetResult();
        AppsChanged?.Invoke();
        CreateWatchers(settings.ExpandedAppDirectories);
    }

    // ── Rescan (triggered by AppDirectories change in Settings) ──────────────

    private void OnAppDirectoriesChanged() {
        if (!_started || !settings.EnableAppSearch) return;
        _ = RescanAsync();
    }

    private async Task RescanAsync() {
        // 1. Cancel any in-flight scan and dispose old watchers
        _liveCts.Cancel();
        _liveCts.Dispose();
        _liveCts = new CancellationTokenSource();
        var ct = _liveCts.Token;

        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();

        // 2. Scan new directories into a temporary dictionary (old cache stays searchable)
        var newDirs = settings.ExpandedAppDirectories;
        logger.LogInformation("AppSearch rescan start dirs=[{Dirs}]", string.Join(", ", newDirs));

        var scanned = new ConcurrentDictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
        await platform.ScanAppsAsync(path => {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name))
                scanned[name] = new AppInfo(name, path);
        }, newDirs, ct);

        if (ct.IsCancellationRequested) return;

        // 3. Diff against current cache
        var oldKeys = _apps.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newKeys = scanned.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = newKeys.Except(oldKeys, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = oldKeys.Except(newKeys, StringComparer.OrdinalIgnoreCase).ToList();

        // 4. Apply removals
        foreach (var key in removed)
            _apps.TryRemove(key, out _);

        // 5. Apply additions (genuinely new apps)
        foreach (var key in added) {
            var app = scanned[key];
            _apps[key] = app;
            iconCache.PreloadAsync(app.Path);
            AppAdded?.Invoke(app);
        }

        // 6. Update existing apps whose path changed (same name, different location)
        foreach (var key in newKeys.Intersect(oldKeys, StringComparer.OrdinalIgnoreCase)) {
            var newApp = scanned[key];
            var oldApp = _apps[key];
            if (!string.Equals(oldApp.Path, newApp.Path, StringComparison.OrdinalIgnoreCase)) {
                _apps[key] = newApp;
                iconCache.Reload(newApp.Path);
            }
        }

        logger.LogInformation("AppSearch rescan done apps={Count} added={Added} removed={Removed}",
            _apps.Count, added.Count, removed.Count);

        // 7. Create new watchers and notify
        CreateWatchers(newDirs);
        AppsChanged?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CreateWatchers(IReadOnlyList<string> dirs) {
        foreach (var w in platform.CreateAppWatchers(dirs,
            path => { AppsChanged?.Invoke(); AddApp(path); },
            path => { AppsChanged?.Invoke(); RemoveApp(path); }))
            _watchers.Add(w);
    }

    private void AddApp(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return;
        var isNew = !_apps.ContainsKey(name);
        var app = new AppInfo(name, path);
        _apps[name] = app;
        if (isNew) {
            iconCache.PreloadAsync(path);
            AppAdded?.Invoke(app);
        } else {
            // Bundle may have changed (e.g. still being copied when first detected) — force icon reload
            iconCache.Reload(path);
        }
    }

    private void RemoveApp(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        _apps.TryRemove(name, out _);
    }

    public void Dispose() {
        settings.AppDirectoriesChanged -= OnAppDirectoriesChanged;
        Stop().GetAwaiter().GetResult();
    }
}

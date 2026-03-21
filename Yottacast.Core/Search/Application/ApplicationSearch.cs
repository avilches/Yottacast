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
/// BrowserDiscovery and TerminalDiscovery query this store instead of hitting the filesystem themselves.
/// </summary>
public sealed class ApplicationSearch(UserSettings settings, PlatformProvider platform, ILogger<ApplicationSearch> logger)
    : IInstantSearchSource, IDisposable {
    private readonly ConcurrentDictionary<string, AppInfo> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    public event Action<AppInfo>? AppAdded;

    private CancellationTokenSource _liveCts = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private TaskCompletionSource _readyTcs = new();

    // ── IInstantSearchSource ──────────────────────────────────────────────────

    public void Start() {
        if (_started) return;
        _started = true;
        _ = ScanAndWatchAsync();
    }

    public Task WhenReady() => _readyTcs.Task;

    public Task Stop() {
        _started = false;
        _liveCts.Cancel();
        _liveCts.Dispose();
        _liveCts = new CancellationTokenSource();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _apps.Clear();
        _readyTcs = new TaskCompletionSource();
        return Task.CompletedTask;
    }

    public IReadOnlyList<ResultItemViewModel> Search(string query, int limit) {
        var results = _apps.Values
            .Select(a => (app: a, score: NameMatcher.Score(a.Name, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => new ResultItemViewModel {
                Icon = "📱",
                Title = x.app.Name,
                Subtitle = x.app.Path,
                Category = "Applications",
                Score = x.score,
                OnActivate = () => platform.LaunchApp(x.app.Path),
            })
            .ToList();
        logger.LogDebug("AppSearch query=\"{Query}\" cache={CacheCount} results={ResultCount} ready={Ready}",
            query, _apps.Count, results.Count, _readyTcs.Task.IsCompleted);
        return results;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public AppInfo? Find(string name) => _apps.GetValueOrDefault(name);

    public IReadOnlyList<AppInfo> FindAll() => [.. _apps.Values];

    public IReadOnlyList<AppInfo> FindByName(string query) =>
        _apps.Values
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // ── Scan + watch ──────────────────────────────────────────────────────────

    private async Task ScanAndWatchAsync() {
        logger.LogInformation("AppSearch scan start dirs=[{Dirs}]", string.Join(", ", settings.ExpandedAppDirectories));
        await platform.ScanAppsAsync(AddApp, settings.ExpandedAppDirectories, _liveCts.Token);
        logger.LogInformation("AppSearch scan done apps={Count}", _apps.Count);
        _readyTcs.TrySetResult();
        foreach (var w in platform.CreateAppWatchers(settings.ExpandedAppDirectories, AddApp, RemoveApp))
            _watchers.Add(w);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddApp(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return;
        var isNew = !_apps.ContainsKey(name);
        var app = new AppInfo(name, path, platform.GetAppIconPath);
        _apps[name] = app;
        if (isNew) AppAdded?.Invoke(app);
    }

    private void RemoveApp(string path) {
        var name = Path.GetFileNameWithoutExtension(path);
        _apps.TryRemove(name, out _);
    }

    public void Dispose() => Stop().GetAwaiter().GetResult();
}

using System.Collections.Concurrent;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// In-memory cache of all installed applications. Implements <see cref="ISearchSource"/>
/// so it can be registered in <see cref="GlobalSearch"/>.
///
/// Startup is platform-specific: macOS runs mdfind one-shot then FileSystemWatcher;
/// Windows/Linux do a synchronous scan then FileSystemWatcher.
///
/// Call <see cref="Start"/> to begin scanning. Call <see cref="Stop"/> to cancel it.
/// BrowserDiscovery and TerminalDiscovery query this store instead of hitting the filesystem themselves.
/// </summary>
public sealed class ApplicationSearch(UserSettings settings, PlatformProvider platform)
    : ISearchSource, IDisposable {
    private readonly ConcurrentDictionary<string, AppInfo> _apps =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    public event Action<AppInfo>? AppAdded;

    private CancellationTokenSource _liveCts = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private TaskCompletionSource _readyTcs = new();

    // ── ISearchSource ─────────────────────────────────────────────────────────

    public bool IsInstant => true;

    public void Start() {
        if (_started) return;
        _started = true;
        _ = ScanAndWatchAsync();
    }

    public Task Ready() => _readyTcs.Task;

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

    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, int limit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var a in _apps.Values.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit)) {
            ct.ThrowIfCancellationRequested();
            yield return new ResultItemViewModel {
                Icon = "📱",
                Title = a.Name,
                Subtitle = a.Path,
                Category = "Applications",
                Score = 0.5,
                OnActivate = () => platform.LaunchApp(a.Path),
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

    // ── Scan + watch ──────────────────────────────────────────────────────────

    private async Task ScanAndWatchAsync() {
        await platform.ScanAppsAsync(AddApp, settings.AppDirectories, _liveCts.Token);
        _readyTcs.TrySetResult();
        foreach (var w in platform.CreateAppWatchers(settings.AppDirectories, AddApp, RemoveApp))
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
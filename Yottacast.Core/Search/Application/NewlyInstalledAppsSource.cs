using Microsoft.Extensions.Logging;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Application;

/// <summary>
/// IEmptyStateSource that shows apps that were installed while Yottacast was running.
/// Subscribes to AppAdded / IconLoaded after ApplicationSearch is ready, accumulates
/// newly-detected apps in memory, and exposes them via GetResults() while search is idle.
/// Thread-safe: _pending list is protected by _pendingLock to handle concurrent access
/// from FileSystemWatcher threads (OnAppAdded) and UI thread (GetResults).
/// </summary>
public class NewlyInstalledAppsSource(
    ApplicationSearch appSearch,
    ILogger<NewlyInstalledAppsSource> logger) : IEmptyStateSource
{
    private readonly List<AppInfo> _pending = [];
    private readonly object _pendingLock = new();

    public event Action? ResultsChanged;

    public void Start() => _ = StartAsync();

    private async Task StartAsync()
    {
        await appSearch.WhenReady().ConfigureAwait(false);
        appSearch.AppAdded += OnAppAdded;
        appSearch.IconLoaded += OnIconLoaded;
        logger.LogDebug("NewlyInstalledAppsSource: subscribed to AppAdded");
    }

    public Task WhenReady() => Task.CompletedTask;

    public Task Stop()
    {
        appSearch.AppAdded -= OnAppAdded;
        appSearch.IconLoaded -= OnIconLoaded;
        lock (_pendingLock)
        {
            _pending.Clear();
        }
        return Task.CompletedTask;
    }

    /// <summary>No-op: newly installed apps are detected via AppAdded, not on window show.</summary>
    public void OnWindowShown(string? clipboardText) { }

    public void OnSearchStarted()
    {
        lock (_pendingLock)
        {
            _pending.Clear();
        }
        logger.LogDebug("NewlyInstalledAppsSource: cleared (search started)");
    }

    public IReadOnlyList<BaseResultItemViewModel> GetResults()
    {
        lock (_pendingLock)
        {
            return _pending.Select(app => appSearch.CreateResultItem(app)).ToList();
        }
    }

    private void OnAppAdded(AppInfo app)
    {
        lock (_pendingLock)
        {
            _pending.Add(app);
        }
        logger.LogDebug("NewlyInstalledAppsSource: app added \"{Name}\"", app.Name);
        ResultsChanged?.Invoke();
    }

    private void OnIconLoaded() => ResultsChanged?.Invoke();
}

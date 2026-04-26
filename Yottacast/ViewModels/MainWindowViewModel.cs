using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    FileIconCache fileIconCache,
    UserDocumentSearch userDocumentSearch,
    UpdateChecker updateChecker,
    HistoryService historyService)
    : ViewModelBase {

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private BaseResultItemViewModel? _selectedResult;

    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private bool _showNoResults;

    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isAltPressed;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private string? _searchHint;

    public bool IsEmojiMode => SelectedResult is EmojiGridResultViewModel;
    public string MetaSymbol => AppHandler.Instance.MetaSymbol;
    public string ShiftSymbol => AppHandler.Instance.ShiftSymbol;

    public ObservableCollection<BaseResultItemViewModel> Results { get; } = [];

    public int DisplayResultCount =>
        Results.OfType<EmojiGridResultViewModel>().FirstOrDefault()?.Cells.Count
        ?? Results.Count;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _deferredCts;

    private IReadOnlyList<BaseResultItemViewModel> _instantSnapshot = [];
    private IReadOnlyList<BaseResultItemViewModel> _deferredSnapshot = [];
    private bool _userNavigated;
    private int _historyNavIndex = -1;
    private bool _navigatingHistory;

    public bool UserNavigated => _userNavigated;

    private readonly List<AppInfo> _pendingAppInfos = [];
    private bool _appCacheRefreshPending;

    public void CancelDeferredSearch() => _deferredCts?.Cancel();
    public void NotifyUserNavigated() => _userNavigated = true;

    public void RefreshSearch() {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        var (items, hint) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
        _instantSnapshot = items;
        SearchHint = hint;
        RefreshResults();
    }

    public void Initialize() {
        _ = CheckForUpdateAsync();
        appSearch.IconLoaded += OnAppCacheChanged;
        appSearch.AppsChanged += OnAppCacheChanged;
        appSearch.AppsChanged += fileIconCache.InvalidateAll;
        appSearch.AppsChanged += userDocumentSearch.InvalidateAll;
        fileIconCache.IconLoaded += OnFileIconLoaded;
        userDocumentSearch.BadgeIconLoaded += OnBadgeIconLoaded;
        settings.SearchSettingsChanged += OnSearchSettingsChanged;
        _ = StartTrackingNewAppsAsync();
    }

    private async Task StartTrackingNewAppsAsync() {
        await appSearch.WhenReady();
        appSearch.AppAdded += app => Dispatcher.UIThread.Post(() => OnNewAppInstalled(app));
    }

    private void OnNewAppInstalled(AppInfo app) {
        if (string.IsNullOrEmpty(SearchText)) {
            _pendingAppInfos.Add(app);
            ShowPendingApps();
        } else {
            // Usuario buscando activamente — refrescar por si la nueva app coincide con la query
            var (items, hint) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
            _instantSnapshot = items;
            SearchHint = hint;
            RefreshResults();
        }
    }

    private void ShowPendingApps() {
        Results.Clear();
        foreach (var info in _pendingAppInfos)
            Results.Add(appSearch.CreateResultItem(info));
        HasResults = Results.Count > 0;
        ShowNoResults = false;
        OnPropertyChanged(nameof(DisplayResultCount));
        SelectedResult = Results.FirstOrDefault();
    }

    private void OnSearchSettingsChanged() {
        Dispatcher.UIThread.Post(() => {
            if (string.IsNullOrEmpty(SearchText)) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _userNavigated = false;
            _ = SearchAsync(SearchText, _cts.Token);
        });
    }

    private void OnAppCacheChanged() {
        if (_appCacheRefreshPending) return;
        _appCacheRefreshPending = true;
        Dispatcher.UIThread.Post(() => {
            _appCacheRefreshPending = false;
            if (string.IsNullOrEmpty(SearchText)) {
                if (_pendingAppInfos.Count > 0) ShowPendingApps();
                return;
            }
            var (items, hint) = globalSearch.SearchInstant(SearchText, limit: SearchSourceLimit);
            _instantSnapshot = items;
            SearchHint = hint;
            RefreshResults();
        });
    }

    private void OnFileIconLoaded() {
        Dispatcher.UIThread.Post(() => {
            foreach (var item in _deferredSnapshot)
                if (item is ResultItemViewModel r && r.IconBytes is null)
                    r.IconBytes = fileIconCache.Get(r.Subtitle);
            RefreshResults();
        });
    }

    private void OnBadgeIconLoaded() {
        Dispatcher.UIThread.Post(() => {
            foreach (var item in _deferredSnapshot)
                if (item is ResultItemViewModel r && r.BadgeIconBytes is null)
                    r.BadgeIconBytes = userDocumentSearch.GetBadge(r.Subtitle);
            RefreshResults();
        });
    }

    private async Task CheckForUpdateAsync() {
        await updateChecker.CheckAsync().ConfigureAwait(false);
        if (updateChecker.UpdateAvailable && updateChecker.LatestVersion is { } v) {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => {
                UpdateBannerText = $"Yottacast {v} available — click to download";
                UpdateAvailable = true;
            });
        }
    }

    [RelayCommand]
    private void UpdateBannerClick() {
        // Placeholder: conectar a la URL de descarga en el siguiente plan
    }

    private const int SearchSourceLimit = AppDefaults.SearchSourceLimit;

    partial void OnSearchTextChanged(string value) {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _userNavigated = false;
        if (!_navigatingHistory) _historyNavIndex = -1;

        if (string.IsNullOrWhiteSpace(value)) {
            IsSearching = false;
            _instantSnapshot = [];
            _deferredSnapshot = [];
            SearchHint = null;
            ShowPendingApps();
            return;
        }

        _pendingAppInfos.Clear();
        _ = SearchAsync(value, _cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct) {
        _instantSnapshot = [];
        _deferredSnapshot = [];
        RefreshResults();

        // Phase 1: instant sources (in-memory cache) — no delay
        if (ct.IsCancellationRequested) return;
        var (instantItems, hint) = globalSearch.SearchInstant(query, limit: SearchSourceLimit);
        _instantSnapshot = instantItems;
        SearchHint = null;
        RefreshResults();

        // Error hints (e.g. incompatible units) are shown after a delay so they don't flash on every keystroke
        if (hint != null)
            _ = ShowHintAfterDelayAsync(hint, ct);

        // Emoji mode: only instant sources, skip deferred search
        if (query.StartsWith(':')) return;

        // Phase 2: deferred sources (disk) — debounce 250ms before hitting disk
        try {
            await Task.Delay(AppDefaults.SearchDebouncedMs, ct);
        } catch (OperationCanceledException) {
            return;
        }

        var oldDeferredCts = _deferredCts;
        _deferredCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        oldDeferredCts?.Dispose();

        IsSearching = true;
        bool completed = false;
        try {
            await foreach (var snapshot in globalSearch.SearchDeferredAsync(query, limit: SearchSourceLimit, _deferredCts.Token)) {
                _deferredSnapshot = snapshot;
                RefreshResults();
            }
            completed = true;
        } catch (OperationCanceledException) { }
        finally {
            IsSearching = false;
        }

        if (completed) ShowNoResults = Results.Count == 0;
    }

    private async Task ShowHintAfterDelayAsync(string hint, CancellationToken ct) {
        try {
            await Task.Delay(AppDefaults.ErrorHintDelayMs, ct);
            SearchHint = hint;
        } catch (OperationCanceledException) { }
    }

    partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
        OnPropertyChanged(nameof(IsEmojiMode));
    }

    /// <summary>
    /// Single point of history saving. Saves current query if non-empty, then clears the search field.
    /// Call this instead of setting SearchText = "" directly, whenever a search ends.
    /// </summary>
    public void CleanAndSaveHistory(string? actionName) {
        if (!string.IsNullOrWhiteSpace(SearchText))
            historyService.Add(SearchText, actionName);
        SearchText = "";
    }

    /// <summary>
    /// Navigates to a previous history entry (older entries). Maintains _historyNavIndex
    /// across SearchText changes by using _navigatingHistory guard.
    /// </summary>
    public void NavigateHistoryBack() {
        var entries = historyService.Entries;
        if (entries.Count == 0) { _historyNavIndex = -1; return; }
        _historyNavIndex = Math.Min(_historyNavIndex + 1, entries.Count - 1);
        _navigatingHistory = true;
        SearchText = entries[entries.Count - 1 - _historyNavIndex].Query;
        _navigatingHistory = false;
    }

    /// <summary>
    /// Navigates to a more recent history entry (newer entries).
    /// </summary>
    public void NavigateHistoryForward() {
        if (_historyNavIndex <= 0) return;
        _historyNavIndex--;
        var entries = historyService.Entries;
        if (entries.Count == 0) { _historyNavIndex = -1; return; }
        _historyNavIndex = Math.Min(_historyNavIndex, entries.Count - 1);
        _navigatingHistory = true;
        SearchText = entries[entries.Count - 1 - _historyNavIndex].Query;
        _navigatingHistory = false;
    }

    private void RefreshResults() {
        var merged = _instantSnapshot
            .Concat(_deferredSnapshot)
            .OrderByDescending(x => x.Score)
            .ToList();

        var previousSelected = SelectedResult;
        Results.Clear();
        foreach (var item in merged) Results.Add(item);
        HasResults = Results.Count > 0;
        ShowNoResults = false;
        OnPropertyChanged(nameof(DisplayResultCount));

        var calcResult = merged.FirstOrDefault(x =>
            x is ConversionResultItemViewModel ||
            (x is ResultItemViewModel r && r.Category is "Calculator"));
        if (calcResult != null && !_userNavigated) {
            SelectedResult = calcResult;
        } else if (_userNavigated && previousSelected != null && merged.Contains(previousSelected)) {
            SelectedResult = previousSelected;
        } else {
            SelectedResult = Results.FirstOrDefault();
        }
    }

}                                                                     
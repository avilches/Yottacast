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
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    ApplicationSearch appSearch,
    UpdateChecker updateChecker)
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

    public ObservableCollection<BaseResultItemViewModel> Results { get; } = [];

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _deferredCts;

    private IReadOnlyList<BaseResultItemViewModel> _instantSnapshot = [];
    private IReadOnlyList<BaseResultItemViewModel> _deferredSnapshot = [];
    private bool _userNavigated;

    private readonly List<AppInfo> _pendingAppInfos = [];

    public void CancelDeferredSearch() => _deferredCts?.Cancel();
    public void NotifyUserNavigated() => _userNavigated = true;

    public void Initialize() {
        _ = CheckForUpdateAsync();
        appSearch.IconLoaded += OnAppCacheChanged;
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
        SelectedResult = Results.FirstOrDefault();
    }

    private void OnAppCacheChanged() {
        Dispatcher.UIThread.Post(() => {
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
        SearchHint = hint;
        RefreshResults();

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
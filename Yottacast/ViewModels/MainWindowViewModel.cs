using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    BrowserDiscovery browserDiscovery,
    UpdateChecker updateChecker)
    : ViewModelBase {

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private BaseResultItemViewModel? _selectedResult;

    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private bool _showNoResults;

    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private string? _searchHint;

    public ObservableCollection<BaseResultItemViewModel> Results { get; } = [];

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _deferredCts;

    private IReadOnlyList<BaseResultItemViewModel> _instantSnapshot = [];
    private IReadOnlyList<BaseResultItemViewModel> _deferredSnapshot = [];
    private ResultItemViewModel? _googleItem;
    private bool _userNavigated;

    public void CancelDeferredSearch() => _deferredCts?.Cancel();
    public void NotifyUserNavigated() => _userNavigated = true;

    public void Initialize() {
        _ = CheckForUpdateAsync();
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

    /// <summary>
    /// The amount of search per service
    /// </summary>
    private const int SearchSourceLimit = 10;

    partial void OnSearchTextChanged(string value) {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _userNavigated = false;

        if (string.IsNullOrWhiteSpace(value)) {
            IsSearching = false;
            Results.Clear();
            HasResults = false;
            ShowNoResults = false;
            SearchHint = null;
            return;
        }

        _ = SearchAsync(value, _cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct) {
        _instantSnapshot = [];
        _deferredSnapshot = [];
        _googleItem = query.StartsWith(':')
            ? (query.Length > 1 ? MakeGoogleItem(query[1..].Trim()) : null)
            : MakeGoogleItem(query);
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
            await Task.Delay(250, ct);
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
        var merged = (_googleItem != null ? new[] { (BaseResultItemViewModel)_googleItem } : Array.Empty<BaseResultItemViewModel>())
            .Concat(_instantSnapshot)
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

    private ResultItemViewModel MakeGoogleItem(string query) {
        var capturedQuery = query;
        return new ResultItemViewModel {
            Icon = "🔍",
            Score = 3,
            Title = $"Search \"{capturedQuery}\" on Google",
            Subtitle = "Open in browser",
            Category = "Web",
            OnActivate = () => {
                var browser = settings.ActiveBrowser;
                if (browser is null) return;
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(capturedQuery)}";
                browserDiscovery.OpenUrl(url, browser);
            },
        };
    }
}                                                                     
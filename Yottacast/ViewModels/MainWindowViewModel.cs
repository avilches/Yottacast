using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class MainWindowViewModel(
    UserSettings settings,
    GlobalSearch globalSearch,
    BrowserDiscovery browserDiscovery)
    : ViewModelBase {

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private ResultItemViewModel? _selectedResult;

    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private bool _showNoResults;

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    private CancellationTokenSource? _cts;

    private IReadOnlyList<ResultItemViewModel> _instantSnapshot = [];
    private IReadOnlyList<ResultItemViewModel> _deferredSnapshot = [];
    private ResultItemViewModel? _googleItem;

    partial void OnSearchTextChanged(string value) {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(value)) {
            Results.Clear();
            HasResults = false;
            ShowNoResults = false;
            return;
        }

        _ = SearchAsync(value, _cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct) {
        _instantSnapshot = [];
        _deferredSnapshot = [];
        _googleItem = MakeGoogleItem(query);
        RefreshResults();

        // Phase 1: instant sources (in-memory cache) — no delay
        try {
            await foreach (var snapshot in globalSearch.SearchInstantAsync(query, limit: 2, ct)) {
                _instantSnapshot = snapshot;
                RefreshResults();
            }
        } catch (OperationCanceledException) {
            return;
        }

        // Phase 2: deferred sources (disk) — debounce 250ms before hitting disk
        try {
            await Task.Delay(250, ct);
        } catch (OperationCanceledException) {
            return;
        }

        try {
            await foreach (var snapshot in globalSearch.SearchDeferredAsync(query, limit: 10, ct)) {
                _deferredSnapshot = snapshot;
                RefreshResults();
            }
        } catch (OperationCanceledException) {
            return;
        }

        ShowNoResults = Results.Count == 0;
    }

    private void RefreshResults() {
        var merged = new[] { _googleItem! }
            .Concat(_instantSnapshot)
            .Concat(_deferredSnapshot)
            .OrderByDescending(x => x.Score)
            .ToList();

        var previousSelected = SelectedResult;
        Results.Clear();
        foreach (var item in merged) Results.Add(item);
        HasResults = Results.Count > 0;
        ShowNoResults = false;

        SelectedResult = previousSelected != null && merged.Contains(previousSelected)
            ? previousSelected
            : Results.FirstOrDefault();
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

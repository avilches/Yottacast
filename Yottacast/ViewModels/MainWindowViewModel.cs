using System;
using System.Collections.ObjectModel;
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
        Results.Clear();
        HasResults = false;
        ShowNoResults = false;

        var googleItem = MakeGoogleItem(query);
        Results.Add(googleItem);
        HasResults = true;
        SelectedResult = googleItem;

        // Phase 1: instant sources (in-memory cache) — no delay
        try {
            await foreach (var item in globalSearch.SearchInstantAsync(query, limit: 2, ct))
                InsertSorted(item);
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
            await foreach (var item in globalSearch.SearchDeferredAsync(query, limit: 10, ct))
                InsertSorted(item);
        } catch (OperationCanceledException) {
            return;
        }

        HasResults = Results.Count > 0;
        ShowNoResults = !HasResults;
    }

    private void InsertSorted(ResultItemViewModel item) {
        var i = 0;
        while (i < Results.Count && Results[i].Score >= item.Score) i++;
        Results.Insert(i, item);
    }

    private ResultItemViewModel MakeGoogleItem(string query) {
        var capturedQuery = query;
        return new ResultItemViewModel {
            Icon = "🔍",
            Score = 1,
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
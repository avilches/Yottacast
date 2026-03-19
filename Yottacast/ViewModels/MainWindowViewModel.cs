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

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ResultItemViewModel? _selectedResult;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showNoResults;

    public ObservableCollection<ResultItemViewModel> Results { get; } = [];

    private readonly UserSettings _settings;
    private readonly GlobalSearch _globalSearch;
    private readonly BrowserDiscovery _browserDiscovery;
    private CancellationTokenSource? _cts;

    public MainWindowViewModel(
        UserSettings settings, GlobalSearch globalSearch, BrowserDiscovery browserDiscovery) {
        _settings = settings;
        _globalSearch = globalSearch;
        _browserDiscovery = browserDiscovery;
    }

    partial void OnSearchTextChanged(string value)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = SearchAsync(value, _cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        Results.Clear();
        HasResults = false;
        ShowNoResults = false;

        if (string.IsNullOrWhiteSpace(query)) return;

        // Immediately show Google search option
        var googleItem = MakeGoogleItem(query);
        Results.Add(googleItem);
        HasResults = true;
        SelectedResult = googleItem;

        // Debounce before hitting the filesystem / cache
        try { await Task.Delay(250, ct); } catch (OperationCanceledException) { return; }

        try {
            await foreach (var item in _globalSearch.SearchAsync(query, limit: 10, ct)) {
                Results.Add(item);
            }
        } catch (OperationCanceledException) {
            return;
        }

        HasResults = Results.Count > 0;
        ShowNoResults = !HasResults;
    }

    private ResultItemViewModel MakeGoogleItem(string query)
    {
        var capturedQuery = query;
        return new ResultItemViewModel
        {
            Icon = "🔍",
            Title = $"Search \"{capturedQuery}\" on Google",
            Subtitle = "Open in browser",
            Category = "Web",
            OnActivate = () =>
            {
                var browser = _settings.ActiveBrowser;
                if (browser is null) return;
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(capturedQuery)}";
                _browserDiscovery.OpenUrl(url, browser);
            },
        };
    }
}

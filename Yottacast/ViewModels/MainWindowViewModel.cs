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
    private CancellationTokenSource? _cts;

    public MainWindowViewModel(UserSettings settings, GlobalSearch globalSearch) {
        _settings = settings;
        _globalSearch = globalSearch;
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
            await foreach (var item in _globalSearch.SearchAsync(query, ct)) {
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
        var browser = _settings.ActiveBrowser;
        var capturedQuery = query;
        return new ResultItemViewModel
        {
            Icon = "🔍",
            Title = $"Search \"{capturedQuery}\" on Google",
            Subtitle = browser is not null ? $"Open in {browser.Name}" : "Open in browser",
            Category = "Web",
            OnActivate = () =>
            {
                var b = _settings.ActiveBrowser;
                if (b is null) return;
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(capturedQuery)}";
                BrowserLauncher.OpenUrl(url, b);
            },
        };
    }
}

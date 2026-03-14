using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly IReadOnlyList<BrowserInfo> _browsers;
    private CancellationTokenSource? _cts;

    public MainWindowViewModel(UserSettings settings) {
        _settings = settings;
        _browsers = BrowserDiscovery.Discover();
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

        // Debounce before hitting the filesystem
        try { await Task.Delay(250, ct); } catch (OperationCanceledException) { return; }

        var files = new System.Collections.Generic.List<FileResult>();
        await FileSearch.SearchAsync(query, files.Add, 15, ct: ct);
        if (ct.IsCancellationRequested) return;

        foreach (var file in files)
            Results.Add(new ResultItemViewModel
            {
                Icon = "📄",
                Title = file.Name,
                Subtitle = file.Path,
                Category = "Files",
            });

        HasResults = Results.Count > 0;
        ShowNoResults = !HasResults;
    }

    private BrowserInfo? GetPreferredBrowser() {
        if (!string.IsNullOrEmpty(_settings.Browser))
            return _browsers.FirstOrDefault(b => b.Name == _settings.Browser)
                   ?? _browsers.FirstOrDefault();
        return _browsers.FirstOrDefault();
    }

    private ResultItemViewModel MakeGoogleItem(string query)
    {
        var capturedQuery = query;
        return new ResultItemViewModel
        {
            Icon = "🔍",
            Title = $"Search \"{capturedQuery}\" on Google",
            Subtitle = _browsers.Count > 0 ? $"Open in {GetPreferredBrowser()?.Name ?? "browser"}" : "Open in browser",
            Category = "Web",
            OnActivate = () =>
            {
                var browser = GetPreferredBrowser();
                if (browser is null) return;
                var url = $"https://www.google.com/search?q={Uri.EscapeDataString(capturedQuery)}";
                BrowserLauncher.OpenUrl(url, browser);
            },
        };
    }
}

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
using Yottacast.Core.Search.Url;
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
    HistoryService historyService,
    UrlSearch urlSearch,
    LaunchHistory launchHistory,
    IEnumerable<IEmptyStateSource> emptySources)
    : ViewModelBase {

    private readonly IReadOnlyList<IEmptyStateSource> _emptySources = emptySources.ToList();

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private BaseResultItemViewModel? _selectedResult;

    [ObservableProperty] private bool _hasResults;

    [ObservableProperty] private bool _showNoResults;

    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isAltPressed;

    [ObservableProperty] private bool _isOptionsMenuOpen;
    [ObservableProperty] private int _optionsMenuSelectedIndex;

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateBannerText = "";
    [ObservableProperty] private string? _searchHint;
    [ObservableProperty] private bool _searchHintIsError;
    [ObservableProperty] private bool _searchHintIsInfo;

    public bool IsEmojiMode => SelectedResult is EmojiGridResultViewModel;
    public string MetaSymbol => AppHandler.Instance.MetaSymbol;
    public string ShiftSymbol => AppHandler.Instance.ShiftSymbol;
    public string SettingsShortcutText => $"{MetaSymbol},  settings";

    public IReadOnlyList<string> FooterHints {
        get {
            var actions = SelectedResult?.Actions;
            if (actions is null or { Count: 0 }) return ["Esc  clear"];

            var hints = new List<string>();
            foreach (var a in actions.Where(a => a.ShowInFooter && a.Hotkey != null))
                hints.Add($"{AppHandler.Instance.FormatHotkey(a.Hotkey!)}  {a.Label}");

            if (actions.Any(a => a.ShowInMenu))
                hints.Add("Tab  Options");

            hints.Add("Esc  clear");
            return hints;
        }
    }

    public IReadOnlyList<ResultAction> OptionsMenuActions {
        get {
            var actions = SelectedResult?.Actions;
            if (actions == null) return [];
            return actions.Where(a => a.ShowInMenu).ToList();
        }
    }

    public IReadOnlyList<OptionsMenuItemVm> OptionsMenuItems =>
        OptionsMenuActions.Select(a => new OptionsMenuItemVm(
            Label: a.Label,
            FormattedHotkey: a.Hotkey != null ? AppHandler.Instance.FormatHotkey(a.Hotkey) : null
        )).ToList();

    public bool HasOptionsMenu => OptionsMenuActions.Count > 0;

    public ResultAction? SelectedMenuAction =>
        IsOptionsMenuOpen && OptionsMenuSelectedIndex < OptionsMenuActions.Count
            ? OptionsMenuActions[OptionsMenuSelectedIndex]
            : null;

    public void OpenOptionsMenu() {
        if (!HasOptionsMenu) return;
        IsOptionsMenuOpen = true;
        OptionsMenuSelectedIndex = 0;
    }

    public void CloseOptionsMenu() {
        IsOptionsMenuOpen = false;
        OptionsMenuSelectedIndex = 0;
    }

    public void NavigateOptionsMenu(int delta) {
        var count = OptionsMenuActions.Count;
        if (count == 0) return;
        OptionsMenuSelectedIndex = ((OptionsMenuSelectedIndex + delta) % count + count) % count;
    }

    public ObservableCollection<BaseResultItemViewModel> Results { get; } = [];

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _deferredCts;
    private CancellationTokenSource? _decayCts;
    private CancellationTokenSource? _copiedMsgCts;

    private IReadOnlyList<BaseResultItemViewModel> _instantSnapshot = [];
    private IReadOnlyList<BaseResultItemViewModel> _deferredSnapshot = [];
    private bool _userNavigated;
    private int _historyNavIndex = -1;
    private bool _navigatingHistory;
    private bool _textIsFromHistory;

    public bool UserNavigated => _userNavigated;

    private bool _appCacheRefreshPending;

    public void CancelDeferredSearch() => _deferredCts?.Cancel();
    public void NotifyUserNavigated() => _userNavigated = true;

    public void RefreshSearch() {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText.Trim(), limit: SearchSourceLimit);
        _instantSnapshot = items;
        SetSearchHint(hint, hintKind);
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
        urlSearch.ResultChanged += OnUrlResultChanged;
        foreach (var source in _emptySources)
        {
            source.Start();
            source.ResultsChanged += () => Dispatcher.UIThread.Post(() => {
                if (string.IsNullOrEmpty(SearchText)) RefreshEmptyState();
            });
        }
    }

    private void OnSearchSettingsChanged() {
        Dispatcher.UIThread.Post(() => {
            if (string.IsNullOrEmpty(SearchText)) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _userNavigated = false;
            _ = SearchAsync(SearchText.Trim(), _cts.Token);
        });
    }

    private void OnAppCacheChanged() {
        if (_appCacheRefreshPending) return;
        _appCacheRefreshPending = true;
        Dispatcher.UIThread.Post(() => {
            _appCacheRefreshPending = false;
            if (string.IsNullOrEmpty(SearchText)) {
                RefreshEmptyState();
                return;
            }
            var (items, hint, hintKind) = globalSearch.SearchInstant(SearchText.Trim(), limit: SearchSourceLimit);
            _instantSnapshot = items;
            SetSearchHint(hint, hintKind);
            RefreshResults();
        });
    }

    private void OnFileIconLoaded() {
        Dispatcher.UIThread.Post(() => {
            foreach (var item in _instantSnapshot.Concat(_deferredSnapshot))
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

    private void OnUrlResultChanged() {
        Dispatcher.UIThread.Post(RefreshSearch);
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
        if (!_navigatingHistory) { _historyNavIndex = -1; _textIsFromHistory = false; }

        if (string.IsNullOrWhiteSpace(value)) {
            IsSearching = false;
            _instantSnapshot = [];
            _deferredSnapshot = [];
            SetSearchHint(null);
            RefreshEmptyState();
            return;
        }

        foreach (var source in _emptySources) source.OnSearchStarted();
        _ = SearchAsync(value.Trim(), _cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct) {
        _instantSnapshot = [];
        _deferredSnapshot = [];
        RefreshResults();

        // Phase 1: instant sources (in-memory cache) — no delay
        if (ct.IsCancellationRequested) return;
        var (instantItems, hint, hintKind) = globalSearch.SearchInstant(query, limit: SearchSourceLimit);
        _instantSnapshot = instantItems;
        SetSearchHint(null);
        RefreshResults();

        // Error hints (e.g. incompatible units) are shown after a delay so they don't flash on every keystroke
        if (hint != null)
            _ = ShowHintAfterDelayAsync(hint, hintKind, ct);

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

    private void SetSearchHint(string? text, SearchHintKind kind = SearchHintKind.Info) {
        SearchHint = text;
        SearchHintIsError = text != null && kind == SearchHintKind.Error;
        SearchHintIsInfo  = text != null && kind == SearchHintKind.Info;
    }

    private async Task ShowHintAfterDelayAsync(string hint, SearchHintKind kind, CancellationToken ct) {
        try {
            await Task.Delay(AppDefaults.ErrorHintDelayMs, ct);
            SetSearchHint(hint, kind);
        } catch (OperationCanceledException) { }
    }

    partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
        OnPropertyChanged(nameof(IsEmojiMode));
        OnPropertyChanged(nameof(FooterHints));
        OnPropertyChanged(nameof(OptionsMenuActions));
        OnPropertyChanged(nameof(OptionsMenuItems));
        OnPropertyChanged(nameof(HasOptionsMenu));
        CloseOptionsMenu();
    }

    /// <summary>
    /// Single point of history saving. Saves current query if non-empty, then clears the search field.
    /// Call this instead of setting SearchText = "" directly, whenever a search ends.
    /// </summary>
    public void CleanAndSaveHistory(string? actionName) {
        if (!string.IsNullOrWhiteSpace(SearchText) && !_textIsFromHistory)
            historyService.Add(SearchText.Trim(), actionName);
        SearchText = "";
    }

    /// <summary>
    /// Navigates to a previous history entry (older entries). Maintains _historyNavIndex
    /// across SearchText changes by using _navigatingHistory guard.
    /// </summary>
    public void NavigateHistoryBack() {
        if (!settings.EnableHistory) return;
        var entries = historyService.Entries;
        if (entries.Count == 0) { _historyNavIndex = -1; return; }
        _historyNavIndex = Math.Min(_historyNavIndex + 1, entries.Count - 1);
        _navigatingHistory = true;
        SearchText = entries[entries.Count - 1 - _historyNavIndex].Query;
        _navigatingHistory = false;
        _textIsFromHistory = true;
    }

    /// <summary>
    /// Navigates to a more recent history entry (newer entries).
    /// </summary>
    public void NavigateHistoryForward() {
        if (!settings.EnableHistory) return;
        if (_historyNavIndex <= 0) return;
        _historyNavIndex--;
        var entries = historyService.Entries;
        if (entries.Count == 0) { _historyNavIndex = -1; return; }
        _historyNavIndex = Math.Min(_historyNavIndex, entries.Count - 1);
        _navigatingHistory = true;
        SearchText = entries[entries.Count - 1 - _historyNavIndex].Query;
        _navigatingHistory = false;
        _textIsFromHistory = true;
    }

    /// <summary>
    /// Starts (or resets) the decay timer. When it fires, clears the search text as if Escape was pressed.
    /// No-op if KeepValueWhenHide is false or duration is 0 (Always).
    /// </summary>
    public void StartDecayTimer() {
        _decayCts?.Cancel();
        _decayCts?.Dispose();
        _decayCts = null;

        if (!settings.KeepValueWhenHide || settings.KeepValueWhenHideDuration <= 0) return;

        var cts = new CancellationTokenSource();
        _decayCts = cts;
        var delay = TimeSpan.FromSeconds(settings.KeepValueWhenHideDuration);

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, cts.Token);
                Dispatcher.UIThread.Post(() => CleanAndSaveHistory(null));
            } catch (OperationCanceledException) {
                // Timer cancelled — keep the value
            }
        });
    }

    /// <summary>
    /// Cancels any pending decay timer, preserving the current search text.
    /// </summary>
    public void CancelDecayTimer() {
        _decayCts?.Cancel();
        _decayCts?.Dispose();
        _decayCts = null;
    }

    public void ShowCopiedMessage(string msg) {
        _copiedMsgCts?.Cancel();
        _copiedMsgCts = new CancellationTokenSource();
        SetSearchHint(msg, SearchHintKind.Info);
        _ = ClearCopiedMessageAsync(msg, _copiedMsgCts.Token);
    }

    private async Task ClearCopiedMessageAsync(string msg, CancellationToken ct) {
        try {
            await Task.Delay(AppDefaults.CopiedMessageDurationMs, ct);
            if (SearchHint == msg) SetSearchHint(null);
        } catch (OperationCanceledException) { }
    }

    public void RecordLaunch(BaseResultItemViewModel item) {
        if (item is ResultItemViewModel r && !string.IsNullOrEmpty(r.ItemPath))
            launchHistory.Record(r.ItemPath);
    }

    /// <summary>
    /// Called by MainWindow when the window becomes visible with empty search text.
    /// clipboardText is the raw clipboard string read by the View layer.
    /// </summary>
    public void OnWindowShown(string? clipboardText)
    {
        foreach (var source in _emptySources)
            source.OnWindowShown(clipboardText);
        RefreshEmptyState();
    }

    private void RefreshEmptyState()
    {
        var results = _emptySources.SelectMany(s => s.GetResults()).ToList();
        Results.Clear();
        foreach (var r in results) Results.Add(r);
        HasResults = Results.Count > 0;
        ShowNoResults = false;
        SelectedResult = Results.FirstOrDefault();
    }

    private void RefreshResults() {
        var merged = _instantSnapshot
            .Concat(_deferredSnapshot)
            .Select(x => (item: x, score: x.Score + launchHistory.BonusFor(x)))
            .OrderByDescending(x => x.score)
            .Select(x => x.item)
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

/// <summary>Display model for a single item in the options overlay menu.</summary>
public sealed record OptionsMenuItemVm(string Label, string? FormattedHotkey);                                                                     
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Date;
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
    DateSearch dateSearch,
    LaunchHistory launchHistory,
    FileEditorService fileEditorService,
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

    [ObservableProperty] private bool _isEditorOpen;

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
            if (actions is null or { Count: 0 }) return [];

            var hints = new List<string>();
            foreach (var a in actions.Where(a => a.ShowInFooter && a.Hotkey != null))
                hints.Add($"{AppHandler.Instance.FormatHotkey(a.Hotkey!)}  {a.LabelProvider?.Invoke() ?? a.Label}");

            if (actions.Any(a => a.ShowInMenu))
                hints.Add("Tab  Options");

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
            Label: a.LabelProvider?.Invoke() ?? a.Label,
            FormattedHotkey: a.Hotkey != null ? AppHandler.Instance.FormatHotkey(a.Hotkey) : null
        )).ToList();

    public bool HasOptionsMenu => OptionsMenuActions.Count > 0;

    public ResultAction? SelectedMenuAction =>
        IsOptionsMenuOpen && OptionsMenuSelectedIndex < OptionsMenuActions.Count
            ? OptionsMenuActions[OptionsMenuSelectedIndex]
            : null;

    public void OpenOptionsMenu() {
        if (!HasOptionsMenu) return;
        OptionsMenuSelectedIndex = -1; // force property-changed so 0 triggers a real update
        IsOptionsMenuOpen = true;
        OptionsMenuSelectedIndex = 0;
    }

    public void CloseOptionsMenu() {
        IsOptionsMenuOpen = false;
        OptionsMenuSelectedIndex = 0;
        if (_pendingResultsRefresh) {
            _pendingResultsRefresh = false;
            RefreshResults();
        }
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
    private bool _pendingResultsRefresh;
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
        EditorPanel.CloseRequested = () => IsEditorOpen = false;
        _ = CheckForUpdateAsync();
        appSearch.IconLoaded += OnAppCacheChanged;
        appSearch.AppsChanged += OnAppCacheChanged;
        appSearch.AppsChanged += fileIconCache.InvalidateAll;
        appSearch.AppsChanged += userDocumentSearch.InvalidateAll;
        fileIconCache.IconLoaded += OnFileIconLoaded;
        userDocumentSearch.BadgeIconLoaded += OnBadgeIconLoaded;
        settings.SearchSettingsChanged += OnSearchSettingsChanged;
        urlSearch.ResultChanged += OnUrlResultChanged;
        dateSearch.ResultChanged += OnDateResultChanged;
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
            // App-name resolution may also have completed: re-evaluate the dynamic Open label
            // for the currently selected item (RefreshResults reassigns SelectedResult, but
            // CommunityToolkit setters short-circuit on equal references).
            OnPropertyChanged(nameof(FooterHints));
            OnPropertyChanged(nameof(OptionsMenuItems));
            RefreshResults();
        });
    }

    private void OnUrlResultChanged() {
        Dispatcher.UIThread.Post(RefreshSearch);
    }

    private void OnDateResultChanged() {
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

    private string? _savedHintText;
    private bool _savedHintIsError;
    private bool _savedHintIsInfo;
    private bool _dragHintActive;

    public void BeginDragHint() {
        if (_dragHintActive) return;
        _dragHintActive = true;
        _savedHintText = SearchHint;
        _savedHintIsError = SearchHintIsError;
        _savedHintIsInfo = SearchHintIsInfo;
        var meta = AppHandler.Instance.MetaSymbol;
        var alt  = AppHandler.Instance.AltSymbol;
        SetSearchHint($"{meta} Mover   {alt} Copiar   {meta}{alt} Alias", SearchHintKind.Info);
    }

    public void EndDragHint() {
        if (!_dragHintActive) return;
        _dragHintActive = false;
        SearchHint = _savedHintText;
        SearchHintIsError = _savedHintIsError;
        SearchHintIsInfo = _savedHintIsInfo;
        _savedHintText = null;
    }

    partial void OnSelectedResultChanged(BaseResultItemViewModel? value) {
        OnPropertyChanged(nameof(IsEmojiMode));
        OnPropertyChanged(nameof(FooterHints));
        OnPropertyChanged(nameof(OptionsMenuActions));
        OnPropertyChanged(nameof(OptionsMenuItems));
        OnPropertyChanged(nameof(HasOptionsMenu));
        if (!HasOptionsMenu) CloseOptionsMenu();
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

    public EditorPanelViewModel EditorPanel { get; } = new EditorPanelViewModel(fileEditorService);

    public void OpenEditor(string path) {
        EditorPanel.Load(path, settings.FileEditorAutoSave);
        IsEditorOpen = true;
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
        // While the options menu is open, don't touch Results: clearing and rebuilding the
        // collection resets Avalonia's SelectionModel and dispatches async nulls through the
        // SelectedItem binding, which closes the menu regardless of any suppress flags.
        // Accumulate snapshots and apply them all at once when the menu closes.
        if (IsOptionsMenuOpen) {
            _pendingResultsRefresh = true;
            return;
        }

        var merged = _instantSnapshot
            .Concat(_deferredSnapshot)
            .Select(x => {
                var (bonus, count, ageDays) = launchHistory.BonusInfoFor(x);
                x.FrequencyBonus = bonus;

                x.ScoreDisplayText = $"{x.Score + bonus:F2}";

                var reason    = string.IsNullOrEmpty(x.ScoreReason) ? "—" : x.ScoreReason;
                var bonusLine = bonus > 0.001
                    ? $"+{bonus:F2}: {count} lanzamiento{(count != 1 ? "s" : "")}, hace {(int)ageDays} día{((int)ageDays != 1 ? "s" : "")}"
                    : "Sin historial de uso";
                x.ScoreTooltipText = $"Score {x.Score:F2}: {reason}\n{bonusLine}";

                return (item: x, score: x.Score + bonus);
            })
            .OrderByDescending(x => x.score)
            .Select(x => x.item)
            .ToList();

        // Deduplicate: remove file results whose stem matches an app already in the list
        var appNames = merged
            .OfType<ResultItemViewModel>()
            .Where(x => x.Category == "Application")
            .Select(x => x.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (appNames.Count > 0)
            merged.RemoveAll(x =>
                x is ResultItemViewModel { Category: "Files" } file &&
                appNames.Contains(Path.GetFileNameWithoutExtension(file.Title)));

        var previousSelected = SelectedResult;
        Results.Clear();
        foreach (var item in merged) Results.Add(item);
        HasResults = Results.Count > 0;
        ShowNoResults = false;

        BaseResultItemViewModel? chosen;
        var calcResult = merged.FirstOrDefault(x =>
            x is ConversionResultItemViewModel ||
            (x is ResultItemViewModel r && r.Category is "Calculator"));
        if (calcResult != null && !_userNavigated) {
            chosen = calcResult;
        } else if (_userNavigated && previousSelected != null) {
            // Reference match first; semantic fallback when ViewModels are recreated
            // (e.g. app cache refresh creates new objects for the same logical items).
            if (merged.Contains(previousSelected)) {
                chosen = previousSelected;
            } else {
                var prevSubtitle = (previousSelected as ResultItemViewModel)?.Subtitle;
                chosen = merged.FirstOrDefault(x =>
                    x.Title == previousSelected.Title &&
                    (prevSubtitle == null || (x as ResultItemViewModel)?.Subtitle == prevSubtitle));
            }
            chosen ??= Results.FirstOrDefault();
        } else {
            chosen = Results.FirstOrDefault();
        }
        SelectedResult = chosen;

        // Guard: Avalonia may dispatch a null back through the SelectedItem binding after
        // Results.Clear(). Re-assert the desired selection after all pending binding updates.
        if (chosen != null) {
            var target = chosen;
            Dispatcher.UIThread.Post(() => {
                if (SelectedResult == null && Results.Contains(target))
                    SelectedResult = target;
            });
        }
    }

}

/// <summary>Display model for a single item in the options overlay menu.</summary>
public sealed record OptionsMenuItemVm(string Label, string? FormattedHotkey);                                                                     
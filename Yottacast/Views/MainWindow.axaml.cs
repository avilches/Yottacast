using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    private readonly UserSettings _settings;
    private readonly ILogger<MainWindow> _logger;
    private readonly FileEditorService _fileEditorService;
    private bool _cursorHidden;
    private KeyModifiers _lastClickModifiers;
    private bool _dragging;
    private PixelPoint _screenPosAtHide;
    private bool _screenPosKnown;
    private bool _positionDirty;
    private (Point Origin, BaseResultItemViewModel Vm)? _dragCandidate;
    private long _dragCandidateTicks;
    private CancellationTokenSource? _dragTimerCts;
    private PointerEventArgs? _lastDragPointerArgs;
    private Point _rightClickPos;
    private bool _menuOpenedByKeyboard;
    private bool _isEditorPreviewSplit;
    private bool _wasInEditMode;
    private string? _searchTextAtTunnelKey;

    // Required by Avalonia's XAML resource loader; the app always uses the parameterized constructor.
    public MainWindow() : this(null!, null!, null!) { }

    public MainWindow(UserSettings settings, ILogger<MainWindow> logger, FileEditorService fileEditorService) {
        _settings = settings;
        _logger = logger;
        _fileEditorService = fileEditorService;
        InitializeComponent();
        Opened += (_, _) => FocusCorrectControl();
        // Restore focus to SearchBox when the window regains key status (e.g. after
        // MacAppHandler's makeKeyWindow call re-makes us key without activating the app).
        Activated += (_, _) => {
            FocusCorrectControl();
            if (DataContext is MainWindowViewModel vm)
                vm.CancelDecayTimer();
        };
        // Intercept LEFT/RIGHT in the tunnel phase so items with OnLeft/OnRight
        // can capture them before the TextBox moves its cursor.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        // Delete/Back on SearchBox: intercept at the SearchBox level in the tunnel phase so
        // the event is handled before TextBox's bubble class-handler can mark it as Handled.
        // Without this, TextBox consumes Delete/Back even on an empty field, and the Window
        // handlers never get a chance to fire the Delete action on the selected clipboard item.
        SearchBox.AddHandler(KeyDownEvent, OnSearchBoxDeleteHandler, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTunnelPointerMoved, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(Gestures.DoubleTappedEvent, OnResultsDoubleTapped, RoutingStrategies.Bubble);
        ResultsList.AddHandler(PointerPressedEvent, OnResultsPointerPressed, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerMovedEvent, OnResultsPointerMovedForDrag, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerReleasedEvent, OnResultsPointerReleasedForDrag, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerCaptureLostEvent, OnResultsPointerCaptureLostForDrag, RoutingStrategies.Tunnel);
        OptionsMenuList.AddHandler(Gestures.TappedEvent, OnOptionsMenuItemTapped, RoutingStrategies.Bubble);
        OptionsMenuList.AddHandler(PointerMovedEvent, OnOptionsMenuPointerMoved, RoutingStrategies.Bubble);
        PositionChanged += (_, _) => UpdatePositionInMemory();
        // Font diagnostics: log at startup and on first emoji activation. REMOVE after investigation.
        DataContextChanged += (_, _) => {
            if (DataContext is not MainWindowViewModel vm) return;
            LogFontDiagnostics("startup");
            UpdateEditorLayout();
            var emojiLogged = false;
            vm.PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(MainWindowViewModel.IsEmojiMode) && vm.IsEmojiMode && !emojiLogged) {
                    emojiLogged = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => LogFontDiagnostics("after-first-emoji"), Avalonia.Threading.DispatcherPriority.Background);
                }
                if (args.PropertyName == nameof(MainWindowViewModel.IsOptionsMenuOpen) && vm.IsOptionsMenuOpen)
                    Avalonia.Threading.Dispatcher.UIThread.Post(PositionOptionsMenu, Avalonia.Threading.DispatcherPriority.Background);
                if (args.PropertyName == nameof(MainWindowViewModel.IsEditorOpen))
                    UpdateEditorLayout();
            };
            vm.EditorPanel.PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(EditorPanelViewModel.Mode))
                    UpdateEditorLayout();
            };
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) {
            Log($"[Property] IsVisible → {change.NewValue}");
            var isVisible = change.GetNewValue<bool>();
            if (isVisible) {
                // Recompute SearchBox.IsEnabled via UpdateEditorLayout so that it stays
                // disabled when the window reappears in editor mode (instead of forcing true).
                UpdateEditorLayout();
                ApplyPositionOnShow();
                _positionDirty = false;
                _screenPosKnown = false;
                FocusCorrectControl();
                if (DataContext is MainWindowViewModel vm) {
                    vm.CancelDecayTimer();
                    if (string.IsNullOrEmpty(vm.SearchText))
                        _ = HandleWindowShownAsync(vm);
                }
            } else {
                SearchBox.IsEnabled = false;
                SavePosition();
                if (DataContext is MainWindowViewModel vm) {
                    vm.IsAltPressed = false;
                    if (!_settings.KeepValueWhenHide)
                        vm.CleanAndSaveHistory(null);
                    else
                        vm.StartDecayTimer();
                }
            }
        }
    }

    private async Task HandleWindowShownAsync(MainWindowViewModel vm)
    {
        // Show empty state immediately (pending apps, etc.) without waiting for clipboard
        vm.OnWindowShown(null);

        // Then read clipboard and refresh if it contains something useful.
        // Fire-and-forget from OnPropertyChanged: swallow and log any failure so an
        // unobserved exception can never crash the app or be lost silently.
        try {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clipboard != null ? await clipboard.GetTextAsync() : null;
            if (!string.IsNullOrEmpty(text))
                vm.OnWindowShown(text);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to read clipboard on window shown");
        }
    }

    // Wraps the fire-and-forget paste simulation so an exception from the OS keyboard
    // injection can never go unobserved (it would otherwise be lost silently).
    private async Task SimulatePasteSafeAsync() {
        try {
            await AppHandler.Instance.SimulatePasteAsync();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to simulate paste after activating result");
        }
    }

    private void ApplyPositionOnShow() {
        var mousePos = AppHandler.Instance.GetMousePosition();
        var targetScreen = (mousePos.HasValue ? Screens.ScreenFromPoint(mousePos.Value) : null)
                           ?? Screens.Primary
                           ?? Screens.All.FirstOrDefault();

        if (targetScreen == null) return;

        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue) {
            var saved = new PixelPoint(_settings.WindowX.Value, _settings.WindowY.Value);
            if (targetScreen.WorkingArea.Contains(saved)) {
                Position = saved;
                return;
            }
        }

        CenterOnScreen(targetScreen);
    }

    private void CenterOnScreen(Screen screen) {
        var wa = screen.WorkingArea;
        var scaledWidth  = (int)(Width * RenderScaling);
        var scaledHeight = Bounds.Height > 0 ? (int)(Bounds.Height * RenderScaling) : 0;
        var pos = new PixelPoint(
            wa.X + (wa.Width  - scaledWidth)  / 2,
            wa.Y + (wa.Height - scaledHeight) / 3);
        Position = pos;
        Log($"[Position] Centered on screen {ScreenDesc(screen)}: Bounds={Bounds}, scaling={RenderScaling}, scaledW={scaledWidth}, scaledH={scaledHeight} → {pos}");
    }

    // Keeps WindowX/Y in sync in memory on every move (no disk I/O).
    // Marks _positionDirty only when the position actually changes.
    private void UpdatePositionInMemory() {
        if (_settings.WindowX == Position.X && _settings.WindowY == Position.Y) return;
        _settings.WindowX = Position.X;
        _settings.WindowY = Position.Y;
        _positionDirty = true;
    }

    // Persists the current position to disk only if the user moved the window since last save.
    internal void SavePosition() {
        if (!_positionDirty) return;
        Log($"[Position] SavePosition: current Position={Position}");
        _settings.Save();
        _positionDirty = false;
    }

    private static string ScreenDesc(Screen? s) =>
        s == null ? "null" : $"WorkingArea={s.WorkingArea} Scaling={s.Scaling}";

    private void Log(string msg) => _logger.LogDebug("{Msg}", msg);

    private void UpdateEditorLayout() {
        if (DataContext is not MainWindowViewModel vm) return;
        bool isEdit = vm.IsEditorOpen && vm.EditorPanel.IsEditMode;
        if (isEdit) {
            if (!_wasInEditMode)
                _isEditorPreviewSplit = vm.IsPreviewEnabled;
            _wasInEditMode = true;

            Grid.SetColumn(EditorContainer, 0);
            Grid.SetColumnSpan(EditorContainer, 2);
            EditorContainer.Width = GetWindowWidth() + (_isEditorPreviewSplit ? GetPreviewWidth() : 0.0);
            EditorView.Width = double.NaN;
            SearchBox.IsEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => { if (DataContext is MainWindowViewModel v && v.IsEditorOpen && v.EditorPanel.IsEditMode) EditorView.FocusEditor(); },
                Avalonia.Threading.DispatcherPriority.Loaded);
        } else {
            _wasInEditMode = false;
            Grid.SetColumn(EditorContainer, 1);
            Grid.SetColumnSpan(EditorContainer, 1);
            EditorContainer.Width = GetPreviewWidth();
            EditorView.Width = double.NaN;
            SearchBox.IsEnabled = true;
            if (IsVisible) SearchBox.Focus();
        }
    }

    private void ToggleEditorPreviewSplit() {
        _isEditorPreviewSplit = !_isEditorPreviewSplit;
        UpdateEditorLayout();
    }

    private static double GetWindowWidth() =>
        Application.Current?.Resources["Theme.Window.Width"] is double w ? w : AppDefaults.WindowDefaultWidth;

    private static double GetPreviewWidth() =>
        Application.Current?.Resources["Theme.Preview.Width"] is double w ? w : AppDefaults.EditorWidth;

    private void FocusCorrectControl() {
        if (DataContext is MainWindowViewModel vm && vm.IsEditorOpen && vm.EditorPanel.IsEditMode)
            EditorView.FocusEditor();
        else
            SearchBox.Focus();
    }

    // ── Font diagnostics ──────────────────────────────────────────────────────
    // Logs which font Avalonia actually uses to render each keyboard symbol.
    // Used to diagnose the font-change bug that occurs after emoji grid renders.
    // REMOVE after investigation is complete.
    private void LogFontDiagnostics(string context) {
        _logger.LogInformation("[FontDiag] === {Context} ===", context);

        // Part A: which fonts in the tested list have the glyph directly
        var fm = Avalonia.Media.FontManager.Current;
        var symbols = new (uint cp, string label)[] {
            (0x21E7, "⇧"), (0x2318, "⌘"), (0x2191, "↑"),
            (0x2325, "⌥"), (0x2303, "⌃"), (0x21B5, "↵")
        };
        var families = new[] {
            "SF Pro Text", "SF Pro", "SF Pro Display", ".SF NS Text",
            "Apple Symbols", "Helvetica Neue", "Arial", "Arial Unicode MS",
            "Lucida Grande", "New York", "Geneva", "Segoe UI",
            "Apple Color Emoji", "Noto Sans"
        };
        foreach (var (cp, label) in symbols) {
            var found = new System.Collections.Generic.List<string>();
            foreach (var fam in families) {
                try {
                    var tf = new Avalonia.Media.Typeface(fam);
                    if (fm.TryGetGlyphTypeface(tf, out var gt) && gt.GetGlyph(cp) != 0)
                        found.Add(fam);
                } catch { }
            }
            _logger.LogInformation("[FontDiag-A] U+{CP:X4} {Label} has glyph in: {Fonts}",
                cp, label, found.Count > 0 ? string.Join(", ", found) : "(none)");
        }

        // Part B: which typeface Avalonia actually selects when shaping the window font chain
        // NOTE: only run this AFTER emoji loads (not at startup) to avoid cache interference.
        if (context != "startup") {
            try {
                var windowFont = (Avalonia.Media.FontFamily)(Application.Current?.Resources["Theme.Window.FontFamily"]
                                 ?? new Avalonia.Media.FontFamily("SF Pro Text, Segoe UI, Inter"));
                var typeface   = new Avalonia.Media.Typeface(windowFont);
                var testText   = "⇧⌘⌥⌃↑↵";
                var layout     = new Avalonia.Media.TextFormatting.TextLayout(
                    testText, typeface, 13.0,
                    Avalonia.Media.Brushes.Black);
                foreach (var line in layout.TextLines) {
                    foreach (var run in line.TextRuns) {
                        if (run is Avalonia.Media.TextFormatting.ShapedTextRun shaped) {
                            var family = shaped.ShapedBuffer.GlyphTypeface.FamilyName;
                            _logger.LogInformation("[FontDiag-B] Shaped run (len={Len}) → typeface family: {Family}",
                                run.Length, family);
                        }
                    }
                }
            } catch (Exception ex) {
                _logger.LogWarning("[FontDiag-B] TextLayout failed: {Msg}", ex.Message);
            }
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (IsOverInteractiveElement(e.Source as Visual)) return;
        _dragging = true;
        BeginMoveDrag(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) {
        base.OnPointerReleased(e);
        if (_dragging) {
            _dragging = false;
            SavePosition();
        }
    }

    private bool IsOverInteractiveElement(Visual? visual) {
        while (visual != null) {
            if (visual is TextBox or ListBox or ListBoxItem or Button or ScrollViewer)
                return true;
            if (visual == ModePillsContainer)
                return true;
            visual = visual.GetVisualParent();
        }
        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftAlt or Key.RightAlt) {
            if (DataContext is MainWindowViewModel vm) vm.IsAltPressed = false;
        }
    }

    private void ExecuteAction(MainWindowViewModel vm, ResultAction action) {
        var result = vm.SelectedResult;
        action.Execute();

        // Editor hotkeys have no-op Execute; dispatch the real logic here
        if (action.Hotkey == ActionHotkey.MetaP) {
            if (vm.IsEditorOpen && vm.EditorPanel.IsPreviewMode) {
                vm.EditorPanel.RequestClose();
            } else if (!vm.IsEditorOpen && result is ClipboardResultItemViewModel clipRes) {
                vm.EditorPanel.LoadTextContent(clipRes.FullText, clipRes.CopiedAt);
                vm.IsEditorOpen = true;
            } else if (!vm.IsEditorOpen
                && result is FileResultItemViewModel { ItemPath: { } pPath }
                && _fileEditorService.IsTextContent(pPath)) {
                vm.OpenPreview(pPath);
            }
        } else if (action.Hotkey == ActionHotkey.MetaE) {
            if (vm.IsEditorOpen && vm.EditorPanel.IsPreviewMode) {
                var check = _fileEditorService.CanOpen(vm.EditorPanel.FilePath, _settings.FileEditorExtensions);
                if (check.CanOpen) vm.EditorPanel.SwitchToEdit(_settings.FileEditorAutoSave);
                else vm.ShowCopiedMessage(check.Error ?? "Cannot edit this file");
            } else if (!vm.IsEditorOpen && _settings.EnableFileEditor
                && result is FileResultItemViewModel { ItemPath: { } ePath }
                && _fileEditorService.IsTextContent(ePath))
                vm.OpenEditor(ePath);
        }

        if (action.ClosesWindow || action.ClosesMenu)
            vm.CloseOptionsMenu();

        if (action.ClosesWindow) {
            if (result != null) {
                vm.RecordLaunch(result);
                vm.CleanAndSaveHistory(result.Title);
            }
            Hide();
            AppHandler.Instance.OnHide();
            if (action.PasteAfterClose)
                _ = SimulatePasteSafeAsync();
        } else {
            if (result != null && action.RegainFocusAfterExecute)
                vm.RecordLaunch(result);
            var hint = action.HintProvider?.Invoke();
            if (hint != null) vm.ShowCopiedMessage(hint);
            if (action.RegainFocusAfterExecute)
                _ = Task.Delay(AppDefaults.RegainFocusDelayMs)
                    .ContinueWith(_ => Dispatcher.UIThread.Post(() => Activate()), TaskScheduler.Default);
        }
    }

    // Captures the emoji grid state needed to restore cursor position after a RequiresRefresh action.
    private static (EmojiGridResultViewModel? grid, int index, string? selectedChar) CaptureEmojiContext(
        MainWindowViewModel vm, ResultAction action)
    {
        if (!action.RequiresRefresh || vm.SelectedResult is not EmojiGridResultViewModel eg)
            return (null, 0, null);
        var prevSection = eg.SelectedEmojiIndex < eg.Cells.Count
            ? eg.Cells[eg.SelectedEmojiIndex].Section : EmojiSection.Default;
        var selectedChar = prevSection == EmojiSection.Default ? eg.SelectedEmoji?.Char : null;
        return (eg, eg.SelectedEmojiIndex, selectedChar);
    }

    private void ExecuteActionWithContext(MainWindowViewModel vm, ResultAction action) {
        var (grid, idx, chr) = CaptureEmojiContext(vm, action);
        ExecuteAction(vm, action);
        if (action.RequiresRefresh) {
            vm.RefreshSearch();
            RepositionEmojiCursor(vm, grid, idx, chr);
        }
    }

    private static void RepositionEmojiCursor(
        MainWindowViewModel vm,
        EmojiGridResultViewModel? previousGrid,
        int previousIndex,
        string? selectedChar)
    {
        if (previousGrid == null) return;
        if (vm.SelectedResult is not EmojiGridResultViewModel newGrid) return;

        if (selectedChar != null) {
            var idx = newGrid.Cells.ToList()
                .FindIndex(c => c.Char == selectedChar && c.Section == EmojiSection.Default);
            newGrid.SelectedEmojiIndex = idx >= 0 ? idx : Math.Min(previousIndex, newGrid.Cells.Count - 1);
        } else {
            newGrid.SelectedEmojiIndex = Math.Min(previousIndex, newGrid.Cells.Count - 1);
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e) {
        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        // Capture before any processing — OnKeyDown (bubble) uses this to detect if
        // Key.Delete / Key.Back arrived on an already-empty field vs. one the TextBox emptied.
        _searchTextAtTunnelKey = vm.SearchText;

        // Hide cursor on typing; not in Edit mode (user needs to see the text cursor)
        bool isEditMode = vm.IsEditorOpen && vm.EditorPanel.IsEditMode;
        if (!isEditMode && e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)) {
            HideCursor();
        }

        // ── Escape: full cascade in tunnel phase ────────────────────────────────
        // TextBox (SearchBox) and AvaloniaEdit consume Escape in their bubble class-handlers,
        // preventing OnKeyDown (bubble) from ever seeing it — same root cause as Delete/Back.
        // Moving the full cascade here (tunnel) fires before any child class-handler can swallow it.
        if (e.Key == Key.Escape) {
            if (vm.IsOptionsMenuOpen) {
                vm.CloseOptionsMenu();
            } else if (vm.IsEditorOpen && !vm.ClipboardModeActive) {
                // In clipboard mode the preview auto-manages with selection — skip closing it.
                if (vm.EditorPanel.ShowUnsavedDialog)
                    vm.EditorPanel.CancelUnsavedDialog();
                else
                    vm.EditorPanel.RequestClose();
            } else if (vm.IsSearching) {
                vm.CancelDeferredSearch();
                vm.CleanAndSaveHistory(null);
            } else if (!string.IsNullOrEmpty(vm.SearchText)) {
                vm.CleanAndSaveHistory(null);
            } else {
                Hide();
            }
            e.Handled = true;
            return;
        }

        // ── Overlay navigation (when options menu is open) ──────────────────────
        if (vm.IsOptionsMenuOpen) {
            switch (e.Key) {
                case Key.Up:
                    vm.NavigateOptionsMenu(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    vm.NavigateOptionsMenu(+1);
                    e.Handled = true;
                    return;
                case Key.Return:
                    if (vm.SelectedMenuAction is { } menuAction)
                        ExecuteActionWithContext(vm, menuAction);
                    e.Handled = true;
                    return;
                case Key.Tab:
                    vm.CloseOptionsMenu();
                    e.Handled = true;
                    return;
            }
            if (e.Key is Key.Left or Key.Right) { e.Handled = true; return; }
            // Non-navigation keys (e.g. ⌘C, ⌘⇧F) fall through to the action hotkey loop below
        }

        // ── Editor Edit mode: only intercept editor hotkeys; let everything else reach AvaloniaEdit ──
        if (isEditMode) {
            // Escape already handled above by the tunnel cascade block.
            // When the unsaved-changes modal is open, ⌘E means save-and-close
            if (vm.EditorPanel.ShowUnsavedDialog) {
                if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaE)) {
                    vm.EditorPanel.SaveAndClose();
                    e.Handled = true;
                }
                return; // let Space/Enter/Tab reach the focused dialog button
            }
            if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaE)) {
                vm.EditorPanel.SaveAndClose(); // guarda si dirty, cierra siempre, sin popup
                e.Handled = true;
            } else if (!vm.EditorPanel.IsAutoSave && AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaS)) {
                vm.EditorPanel.SaveFile();
                vm.ShowCopiedMessage("Guardado");
                e.Handled = true;
            } else if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaP)) {
                ToggleEditorPreviewSplit();
                e.Handled = true;
            }
            return; // all other keys (arrows, Tab, Enter…) pass through to AvaloniaEdit
        }

        // ── Tab opens overlay ───────────────────────────────────────────────────
        if (e.Key == Key.Tab) {
            if (vm.HasOptionsMenu) {
                _menuOpenedByKeyboard = true;
                vm.OpenOptionsMenu();
            }
            e.Handled = true;
            return;
        }

        // ── Cmd+Option+Right/Left: ciclar modo de búsqueda ──────────────────────
        // Checked before grid nav to prevent Left/Right reaching the switch below.
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaAltRight)) {
            vm.CycleMode();
            e.Handled = true;
            return;
        }
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaAltLeft)) {
            vm.CycleModeBack();
            e.Handled = true;
            return;
        }

        // ── Grid navigation (OnLeft/OnRight/OnUp/OnDown) ────────────────────────
        switch (e.Key) {
            case Key.Left when vm.SelectedResult?.OnLeft is { } onLeft:
                e.Handled = onLeft();
                break;
            case Key.Right when vm.SelectedResult?.OnRight is { } onRight:
                e.Handled = onRight();
                break;
            case Key.Up when vm.SelectedResult?.OnUp is { } onUp:
                e.Handled = onUp();
                break;
            case Key.Down when vm.SelectedResult?.OnDown is { } onDown:
                e.Handled = onDown();
                break;
            case Key.Prior:
                SelectDelta(vm, -GetVisiblePageSize());
                e.Handled = true;
                break;
            case Key.Next:
                SelectDelta(vm, +GetVisiblePageSize());
                e.Handled = true;
                break;
        }

        // ── Cmd+P: toggle preview (clipboard and text files) ────────────────────
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaP)) {
            if (vm.IsEditorOpen && vm.EditorPanel.IsPreviewMode) {
                vm.EditorPanel.RequestClose();
                e.Handled = true;
                return;
            }
            if (!vm.IsEditorOpen && vm.SelectedResult is ClipboardResultItemViewModel clipResult) {
                vm.EditorPanel.LoadTextContent(clipResult.FullText, clipResult.CopiedAt);
                vm.IsEditorOpen = true;
                e.Handled = true;
                return;
            }
            if (!vm.IsEditorOpen
                && vm.SelectedResult is FileResultItemViewModel { ItemPath: { } previewPath }
                && _fileEditorService.IsTextContent(previewPath)) {
                vm.OpenPreview(previewPath);
                e.Handled = true;
                return;
            }
        }

        // ── Cmd+E: open edit directly, or switch preview→edit ────────────────────
        if (AppHandler.Instance.MatchesHotkey(e, ActionHotkey.MetaE)) {
            if (vm.IsEditorOpen) {
                // Edit mode case handled above; here we're always in Preview mode
                var check = _fileEditorService.CanOpen(vm.EditorPanel.FilePath, _settings.FileEditorExtensions);
                if (check.CanOpen) {
                    vm.EditorPanel.SwitchToEdit(_settings.FileEditorAutoSave);
                } else {
                    vm.ShowCopiedMessage(check.Error ?? "Cannot edit this file");
                }
                e.Handled = true;
                return;
            }
            if (_settings.EnableFileEditor
                && vm.SelectedResult is FileResultItemViewModel { ItemPath: { } path }
                && _fileEditorService.IsTextContent(path)) {
                vm.OpenEditor(path);
                e.Handled = true;
                return;
            }
        }

        // ── Generic action hotkeys (excluding Enter, handled in OnKeyDown) ───────
        foreach (var action in vm.SelectedResult?.Actions ?? []) {
            if (action.Hotkey == null || action.Hotkey == ActionHotkey.Enter) continue;

            var matches = AppHandler.Instance.MatchesHotkey(e, action.Hotkey);

            if (!matches) continue;

            // Block Meta+key (e.g. ⌘C) when text is selected in SearchBox
            if (action.Hotkey.Modifiers == ActionModifiers.Meta
                && Math.Abs(SearchBox.SelectionEnd - SearchBox.SelectionStart) > 0)
                continue;

            ExecuteActionWithContext(vm, action);
            e.Handled = true;
            return;
        }
    }

    private void OnSearchBoxDeleteHandler(object? sender, KeyEventArgs e) {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.IsEditorOpen && vm.EditorPanel.IsEditMode) return;
        if (vm.IsOptionsMenuOpen) return;
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (!string.IsNullOrEmpty(vm.SearchText)) return;

        if (e.Key != Key.Delete) return;

        var deleteAction = vm.SelectedResult?.Actions
            .FirstOrDefault(a => a.Hotkey == ActionHotkey.Delete);
        if (deleteAction == null) return;

        ExecuteActionWithContext(vm, deleteAction);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        // On macOS, Cmd+W is the platform "close window" shortcut — hide the launcher instead.
        var (closeMods, closeKey) = AppHandler.Instance.CloseWindowShortcut;
        if (e.Key == closeKey && e.KeyModifiers == closeMods) {
            Hide();
            e.Handled = true;
            return;
        }

        // On macOS, Cmd+Q quits the app entirely. Persist any pending window position and
        // flush buffered logs before exiting, since Environment.Exit skips disposals/finalizers.
        var quitShortcut = AppHandler.Instance.QuitShortcut;
        if (quitShortcut is { } qs && e.Key == qs.Key && e.KeyModifiers == qs.Modifiers) {
            SavePosition();
            Serilog.Log.CloseAndFlush();
            Environment.Exit(0);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        // En modo Edit, solo Esc/Alt/Cmd+, se gestionan aquí; todo lo demás pertenece a AvaloniaEdit
        if (vm.IsEditorOpen && vm.EditorPanel.IsEditMode
            && e.Key is not (Key.Escape or Key.LeftAlt or Key.RightAlt or Key.OemComma)) {
            return;
        }

        switch (e.Key) {

            // Consume ALT+Space so macOS doesn't produce a beep for the unhandled key
            case Key.Space when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                e.Handled = true;
                break;

            case Key.LeftAlt:
            case Key.RightAlt:
                vm.IsAltPressed = true;
                break;

            case Key.Escape:
                if (vm.IsEditorOpen) {
                    if (vm.EditorPanel.ShowUnsavedDialog)
                        vm.EditorPanel.CancelUnsavedDialog();
                    else
                        vm.EditorPanel.RequestClose();
                    e.Handled = true;
                    break;
                }
                if (vm.IsOptionsMenuOpen) {
                    vm.CloseOptionsMenu();
                } else if (vm.IsSearching) {
                    vm.CancelDeferredSearch();
                    vm.CleanAndSaveHistory(null);
                } else if (!string.IsNullOrEmpty(vm.SearchText)) {
                    vm.CleanAndSaveHistory(null);
                } else {
                    Hide();
                }
                e.Handled = true;
                break;

            case Key.Down:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                    if (!e.Handled) {
                        vm.NavigateHistoryForward();
                        SearchBox.CaretIndex = int.MaxValue;
                    }
                } else {
                    SelectNext(vm, +1);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                    if (!e.Handled) {
                        vm.NavigateHistoryBack();
                        SearchBox.CaretIndex = int.MaxValue;
                    }
                } else if (!vm.UserNavigated) {
                    vm.NavigateHistoryBack();
                    SearchBox.CaretIndex = int.MaxValue;
                } else {
                    SelectNext(vm, -1);
                }
                e.Handled = true;
                break;

            case Key.Return:
                if (!vm.IsOptionsMenuOpen) {
                    var enterAction = vm.SelectedResult?.Actions
                        .FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
                    if (enterAction != null) {
                        if (e.KeyModifiers.HasFlag(AppHandler.Instance.MetaKeyModifier)) {
                            // Cmd/Ctrl+Enter: execute without closing the search window
                            ExecuteActionWithContext(vm, enterAction.AsKeepOpen());
                        } else {
                            ExecuteActionWithContext(vm, enterAction);
                        }
                    }
                }
                e.Handled = true;
                break;

            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                (Application.Current as App)?.OpenSettings();
                e.Handled = true;
                break;

            // Delete key: fires the Delete action on the selected result when the search
            // field is empty. Handled here (bubble phase) because TextBox may consume
            // Key.Delete in its own OnKeyDown before the tunnel handler can act.
            // _searchTextAtTunnelKey captures the pre-keystroke value so that the case
            // where TextBox just emptied the field (user deleted the last char) is not
            // confused with "field was already empty → user wants to delete the item".
            case Key.Delete when e.KeyModifiers == KeyModifiers.None: {
                if (string.IsNullOrEmpty(_searchTextAtTunnelKey)) {
                    var deleteAction = vm.SelectedResult?.Actions
                        .FirstOrDefault(a => a.Hotkey == ActionHotkey.Delete);
                    if (deleteAction != null) {
                        ExecuteActionWithContext(vm, deleteAction);
                        e.Handled = true;
                    }
                }
                break;
            }
        }
    }

    // The launcher is a persistent background process — it should never truly close, only hide.
    // This cancels any native close attempt (e.g. macOS performClose: routed here after
    // SettingsWindow closes) and hides the window instead.
    protected override void OnClosing(WindowClosingEventArgs e) {
        e.Cancel = true;
        Hide();
    }

    private void PositionOptionsMenu() {
        if (DataContext is not MainWindowViewModel vm || !vm.IsOptionsMenuOpen || vm.SelectedResult == null)
            return;

        var panelW = ResultsList.Bounds.Width;
        var panelH = ResultsPanel.Bounds.Height;
        var menuW  = OptionsMenuOverlay.Bounds.Width > 0 ? OptionsMenuOverlay.Bounds.Width : AppDefaults.OptionsMenuFallbackWidth;
        var menuH  = OptionsMenuOverlay.Bounds.Height;

        double x, y;
        if (_menuOpenedByKeyboard) {
            x = Math.Max(4, panelW - menuW - 4);
            y = GetSelectedItemY(vm);
        } else {
            x = _rightClickPos.X;
            y = _rightClickPos.Y;
            if (panelW > 0 && x + menuW > panelW - 4)
                x = Math.Max(4, panelW - menuW - 4);
        }

        if (panelH > 0 && menuH > 0 && y + menuH > panelH - 4)
            y = Math.Max(0, panelH - menuH);
        y = Math.Max(0, y);

        OptionsMenuOverlay.Margin = new Thickness(x, y, 0, 0);
    }

    private double GetSelectedItemY(MainWindowViewModel vm) {
        var idx = vm.SelectedResult == null ? -1 : vm.Results.IndexOf(vm.SelectedResult);
        if (idx < 0) return 0;
        if (ResultsList.ContainerFromIndex(idx) is not Control container) return 0;
        var transform = container.TransformToVisual(ResultsPanel);
        if (transform == null) return 0;
        return transform.Value.Transform(new Point(0, 0)).Y;
    }

    private void OnOptionsMenuItemTapped(object? sender, TappedEventArgs e) {
        if (DataContext is not MainWindowViewModel vm) return;
        var lbi = FindListBoxItem(e.Source as Control);
        if (lbi == null) return;

        for (int i = 0; i < OptionsMenuList.ItemCount; i++) {
            if (OptionsMenuList.ContainerFromIndex(i) != lbi) continue;
            vm.OptionsMenuSelectedIndex = i;
            if (vm.SelectedMenuAction is { } action)
                ExecuteActionWithContext(vm, action);
            return;
        }
    }

    private void OnOptionsMenuPointerMoved(object? sender, PointerEventArgs e) {
        if (DataContext is not MainWindowViewModel vm || !vm.IsOptionsMenuOpen) return;
        var lbi = FindListBoxItem(e.Source as Control);
        if (lbi == null) return;
        for (int i = 0; i < OptionsMenuList.ItemCount; i++) {
            if (OptionsMenuList.ContainerFromIndex(i) != lbi) continue;
            vm.OptionsMenuSelectedIndex = i;
            return;
        }
    }

    private void CancelDragTimer() {
        var prevCts = _dragTimerCts;
        _dragTimerCts = null;
        if (prevCts == null) return;
        try {
            prevCts.Cancel();
        } catch (ObjectDisposedException) {
            // already disposed; nothing to cancel
        } finally {
            prevCts.Dispose();
        }
    }

    private void StartDragLongPressTimer(PointerEventArgs triggerEvent, BaseResultItemViewModel candidateVm) {
        var cts = new CancellationTokenSource();
        _dragTimerCts = cts;
        _ = Task.Delay(AppDefaults.DragLongPressMs, cts.Token).ContinueWith(t => {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(async () => {
                if (_dragCandidate?.Vm != candidateVm) return;
                var e = _lastDragPointerArgs;
                if (e == null) return;
                CancelDragTimer();
                await InitiateDragAsync(e, candidateVm);
            });
        }, TaskScheduler.Default);
    }

    private int GetVisiblePageSize() {
        const double itemMinHeight = AppDefaults.ResultItemMinHeight; // matches ListBoxItem MinHeight in Window.Styles
        var sv = ResultsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var viewportHeight = sv?.Viewport.Height ?? ResultsList.Bounds.Height;
        return viewportHeight > 0 ? Math.Max(1, (int)(viewportHeight / itemMinHeight)) : 5;
    }

    private static void SelectNext(MainWindowViewModel vm, int delta) {
        if (vm.Results.Count == 0) return;

        vm.NotifyUserNavigated();
        var current = vm.SelectedResult is null ? -1 : vm.Results.IndexOf(vm.SelectedResult);
        var next = (current + delta + vm.Results.Count) % vm.Results.Count;
        vm.SelectedResult = vm.Results[next];
    }

    private static void SelectDelta(MainWindowViewModel vm, int delta) {
        if (vm.Results.Count == 0) return;
        vm.NotifyUserNavigated();
        var current = vm.SelectedResult is null ? 0 : vm.Results.IndexOf(vm.SelectedResult);
        var next = Math.Clamp(current + delta, 0, vm.Results.Count - 1);
        vm.SelectedResult = vm.Results[next];
    }

    private void TrackOrShowCursor(PointerEventArgs e) {
        var p = e.GetPosition(this);
        var screenPos = new PixelPoint(
            Position.X + (int)Math.Round(p.X * RenderScaling),
            Position.Y + (int)Math.Round(p.Y * RenderScaling));
        if (!_cursorHidden) {
            _screenPosAtHide = screenPos;
            _screenPosKnown = true;
        } else if (!_screenPosKnown) {
            // Primer evento tras ocultar sin anchor conocida: establecer baseline
            // sin mostrar cursor (probablemente causado por resize de ventana).
            _screenPosAtHide = screenPos;
            _screenPosKnown = true;
        } else if (screenPos != _screenPosAtHide) {
            ShowCursor();
        }
    }

    private void OnTunnelPointerMoved(object? sender, PointerEventArgs e) {
        TrackOrShowCursor(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e) {
        base.OnPointerEntered(e);
        TrackOrShowCursor(e);
    }

    private void OnModePillTapped(object? sender, TappedEventArgs e) {
        if (DataContext is not MainWindowViewModel vm) return;
        var source = e.Source as Visual;
        while (source != null) {
            if (source == AllModePill)       { vm.ResetMode();                        break; }
            if (source == FilesModePill)     { vm.ActivateMode(SearchMode.Files);     break; }
            if (source == ClipboardModePill) { vm.ActivateMode(SearchMode.Clipboard); break; }
            source = source.GetVisualParent();
        }
        e.Handled = true;
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e) {
        if (DataContext is not MainWindowViewModel vm) return;

        var enterAction = vm.SelectedResult?.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
        if (enterAction == null) return;

        if (_lastClickModifiers.HasFlag(AppHandler.Instance.MetaKeyModifier)) {
            ExecuteActionWithContext(vm, enterAction.AsKeepOpen());
        } else {
            ExecuteActionWithContext(vm, enterAction);
        }
    }

    private static ListBoxItem? FindListBoxItem(Control? control) {
        Visual? visual = control;
        while (visual != null) {
            if (visual is ListBoxItem item) return item;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private void HideCursor() {
        if (_cursorHidden) return;
        _cursorHidden = true;
        AppHandler.Instance.HideCursor();
        // _screenPosAtHide holds the last screen position tracked while cursor was visible.
        // The OS will send a mouseMoved with that same screen position when the window grows
        // (results appear) — the comparison in TrackOrShowCursor filters it out.
    }

    private void ShowCursor() {
        if (!_cursorHidden) return;
        _cursorHidden = false;
        AppHandler.Instance.ShowCursor();
    }

    private void OnResultsPointerPressed(object? sender, PointerPressedEventArgs e) {
        _lastClickModifiers = e.KeyModifiers;
        var props = e.GetCurrentPoint(ResultsList).Properties;
        var item = FindListBoxItem(e.Source as Control);
        var vm = DataContext as MainWindowViewModel;

        if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) {
            // Set up drag candidate with timestamp and long-press timer
            CancelDragTimer();
            if (item?.DataContext is BaseResultItemViewModel dragVm && dragVm.GetDragPayload is not null) {
                _dragCandidate = (e.GetPosition(ResultsList), dragVm);
                _dragCandidateTicks = Environment.TickCount64;
                _lastDragPointerArgs = e;
                StartDragLongPressTimer(e, dragVm);
            } else {
                _dragCandidate = null;
            }

            // Select the clicked item (close any open menu)
            if (item?.DataContext is BaseResultItemViewModel itemVm && vm != null) {
                if (vm.IsOptionsMenuOpen) vm.CloseOptionsMenu();
                vm.NotifyUserNavigated();
                vm.SelectedResult = itemVm;
            }
        } else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) {
            // Select the item and open the options menu at cursor position
            if (item?.DataContext is BaseResultItemViewModel itemVm && vm != null) {
                vm.NotifyUserNavigated();
                vm.SelectedResult = itemVm;
                if (vm.HasOptionsMenu) {
                    _rightClickPos = e.GetPosition(ResultsPanel);
                    _menuOpenedByKeyboard = false;
                    vm.OpenOptionsMenu();
                    // Always reposition: PropertyChanged on IsOptionsMenuOpen doesn't fire if already open.
                    Dispatcher.UIThread.Post(PositionOptionsMenu, DispatcherPriority.Background);
                }
                e.Handled = true;
            }
        }
    }

    private async void OnResultsPointerMovedForDrag(object? sender, PointerEventArgs e) {
        if (_dragCandidate is not { } candidate) return;
        var props = e.GetCurrentPoint(ResultsList).Properties;
        if (!props.IsLeftButtonPressed) {
            CancelDragTimer();
            _dragCandidate = null;
            return;
        }
        _lastDragPointerArgs = e;
        var current = e.GetPosition(ResultsList);
        var dx = current.X - candidate.Origin.X;
        var dy = current.Y - candidate.Origin.Y;

        bool distanceOk = Math.Abs(dx) >= AppDefaults.DragStartThresholdPx || Math.Abs(dy) >= AppDefaults.DragStartThresholdPx;
        bool timeOk = (Environment.TickCount64 - _dragCandidateTicks) >= AppDefaults.DragMinPressDurationMs;

        if (!distanceOk || !timeOk)
            return;

        CancelDragTimer();
        await InitiateDragAsync(e, candidate.Vm);
    }

    private async Task InitiateDragAsync(PointerEventArgs e, BaseResultItemViewModel candidateVm) {
        // Consume the candidate before awaiting so we don't double-start a drag.
        _dragCandidate = null;

        var vm = DataContext as MainWindowViewModel;
        try {
            var payload = candidateVm.GetDragPayload?.Invoke();
            if (payload is null) return;
            var data = await DragDataFactory.BuildAsync(this, payload);
            if (data is null) {
                _logger.LogDebug("Drag aborted: factory returned null payload for {Type}", payload.GetType().Name);
                return;
            }
            vm?.BeginDragHint();
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Drag-and-drop failed");
        } finally {
            vm?.EndDragHint();
        }
    }

    private void OnResultsPointerReleasedForDrag(object? sender, PointerReleasedEventArgs e) {
        CancelDragTimer();
        _dragCandidate = null;
    }

    private void OnResultsPointerCaptureLostForDrag(object? sender, PointerCaptureLostEventArgs e) {
        CancelDragTimer();
        _dragCandidate = null;
    }

}

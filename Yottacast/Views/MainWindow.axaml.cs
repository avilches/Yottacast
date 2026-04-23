using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    private readonly UserSettings _settings;
    private readonly ILogger<MainWindow> _logger;
    private bool _cursorHidden;
    private bool _dragging;
    private PixelPoint _screenPosAtHide;
    private bool _screenPosKnown;
    private bool _positionDirty;

    // Required by Avalonia's XAML resource loader; the app always uses the parameterized constructor.
    public MainWindow() : this(null!, null!) { }

    public MainWindow(UserSettings settings, ILogger<MainWindow> logger) {
        _settings = settings;
        _logger = logger;
        InitializeComponent();
        Opened += (_, _) => SearchBox.Focus();
        // Restore focus to SearchBox when the window regains key status (e.g. after
        // MacAppHandler's makeKeyWindow call re-makes us key without activating the app).
        Activated += (_, _) => SearchBox.Focus();
        // Intercept LEFT/RIGHT in the tunnel phase so items with OnLeft/OnRight
        // can capture them before the TextBox moves its cursor.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTunnelPointerMoved, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerMovedEvent, OnResultsPointerMoved, RoutingStrategies.Bubble);
        ResultsList.AddHandler(Gestures.TappedEvent, OnResultsTapped, RoutingStrategies.Bubble);
        PositionChanged += (_, _) => UpdatePositionInMemory();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) {
            Log($"[Property] IsVisible → {change.NewValue}");
            var isVisible = change.GetNewValue<bool>();
            SearchBox.IsEnabled = isVisible;
            if (isVisible) {
                ApplyPositionOnShow();
                _positionDirty = false;
                _screenPosKnown = false;
                SearchBox.Focus();
            } else {
                SavePosition(); // flush to disk on hide
                if (DataContext is MainWindowViewModel vm)
                    vm.IsAltPressed = false;
            }
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

    private static bool IsOverInteractiveElement(Visual? visual) {
        while (visual != null) {
            if (visual is TextBox or ListBox or ListBoxItem or Button or ScrollViewer)
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

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e) {
        // Tunnel phase fires before any child handles the event, so we reliably
        // catch character keys that the TextBox would otherwise consume.
        if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)) {
            HideCursor();
        }

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        switch (e.Key) {
            case Key.Left when vm.SelectedResult?.OnLeft is { } onLeft:
                onLeft(); // move cell but don't consume — TextBox also moves its cursor
                break;

            case Key.Right when vm.SelectedResult?.OnRight is { } onRight:
                onRight();
                break;

            case Key.Up when vm.SelectedResult?.OnUp is { } onUp:
                e.Handled = onUp();
                break;

            case Key.Down when vm.SelectedResult?.OnDown is { } onDown:
                e.Handled = onDown();
                break;

            case Key.Prior: // Page Up
                SelectDelta(vm, -AppDefaults.SearchSourceLimit);
                e.Handled = true;
                break;

            case Key.Next: // Page Down
                SelectDelta(vm, +AppDefaults.SearchSourceLimit);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        // On macOS, Cmd+W is the platform "close window" shortcut — hide the launcher instead.
        var (closeMods, closeKey) = AppHandler.Instance.CloseWindowShortcut;
        if (e.Key == closeKey && e.KeyModifiers == closeMods) {
            Hide();
            e.Handled = true;
            return;
        }

        // On macOS, Cmd+Q quits the app entirely.
        var quitShortcut = AppHandler.Instance.QuitShortcut;
        if (quitShortcut is { } qs && e.Key == qs.Key && e.KeyModifiers == qs.Modifiers) {
            Environment.Exit(0);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

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
                if (vm.IsSearching) {
                    vm.CancelDeferredSearch();
                    vm.SearchText = "";
                } else if (!string.IsNullOrEmpty(vm.SearchText)) {
                    vm.SearchText = "";
                } else {
                    Hide();
                }
                e.Handled = true;
                break;

            case Key.Down:
                SelectNext(vm, +1);
                e.Handled = true;
                break;

            case Key.Up:
                SelectNext(vm, -1);
                e.Handled = true;
                break;

            case Key.Return:
                if (vm.SelectedResult is { OnActivate: { } action } result) {
                    action();
                    vm.SearchText = "";
                    Hide();
                    if (result.PasteAfterActivate) {
                        AppHandler.Instance.OnHide();
                        _ = AppHandler.Instance.SimulatePasteAsync();
                    }
                }
                e.Handled = true;
                break;

            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                (Application.Current as App)?.OpenSettings();
                e.Handled = true;
                break;
        }
    }

    // The launcher is a persistent background process — it should never truly close, only hide.
    // This cancels any native close attempt (e.g. macOS performClose: routed here after
    // SettingsWindow closes) and hides the window instead.
    protected override void OnClosing(WindowClosingEventArgs e) {
        e.Cancel = true;
        Hide();
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

    private void OnResultsPointerMoved(object? sender, PointerEventArgs e) {
        if (_cursorHidden) return;
        var item = FindListBoxItem(e.Source as Control);
        if (item?.DataContext is BaseResultItemViewModel itemVm) {
            var vm = DataContext as MainWindowViewModel;
            if (vm is null) return;
            vm.NotifyUserNavigated();
            vm.SelectedResult = itemVm;
        }
    }

    private void OnResultsTapped(object? sender, TappedEventArgs e) {
        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;
        if (vm.SelectedResult is { OnActivate: { } action } result) {
            action();
            vm.SearchText = "";
            Hide();
            if (result.PasteAfterActivate) {
                AppHandler.Instance.OnHide();
                _ = AppHandler.Instance.SimulatePasteAsync();
            }
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
}

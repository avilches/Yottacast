using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yottacast.Core.ViewModels;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    private bool _cursorHidden;
    private PixelPoint _screenPosAtHide;
    private bool _screenPosKnown;

    public MainWindow() {
        InitializeComponent();
        Opened += (_, _) => SearchBox.Focus();
        // Intercept LEFT/RIGHT in the tunnel phase so items with OnLeft/OnRight
        // can capture them before the TextBox moves its cursor.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTunnelPointerMoved, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(PointerMovedEvent, OnResultsPointerMoved, RoutingStrategies.Bubble);
        ResultsList.AddHandler(Gestures.TappedEvent, OnResultsTapped, RoutingStrategies.Bubble);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) {
            var isVisible = change.GetNewValue<bool>();
            SearchBox.IsEnabled = isVisible;
            if (isVisible) {
                _screenPosKnown = false;
                SearchBox.Focus();
            } else if (DataContext is MainWindowViewModel vm) {
                vm.IsAltPressed = false;
            }
        }
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

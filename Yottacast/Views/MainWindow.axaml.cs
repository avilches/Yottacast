using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Yottacast.ViewModels;
using Yottacast;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    private DispatcherTimer? _spinnerTimer;
    private double _spinnerAngle;
    private readonly RotateTransform _spinnerTransform = new();

    public MainWindow() {
        InitializeComponent();
        SpinnerEllipse.RenderTransform = _spinnerTransform;
        Opened += (_, _) => SearchBox.Focus();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is MainWindowViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName != nameof(MainWindowViewModel.IsSearching)) return;
        var active = (sender as MainWindowViewModel)?.IsSearching ?? false;
        if (active) {
            _spinnerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _spinnerTimer.Tick += SpinnerTick;
            _spinnerTimer.Start();
        } else {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;
        }
    }

    private void SpinnerTick(object? sender, EventArgs e) {
        _spinnerAngle = (_spinnerAngle + 8) % 360;
        _spinnerTransform.Angle = _spinnerAngle;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) {
            var isVisible = change.GetNewValue<bool>();
            SearchBox.IsEnabled = isVisible;
            if (isVisible) {
                SearchBox.Focus();
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e) {
        Console.WriteLine($"[Window] OnTextInput text='{e.Text}'");
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        switch (e.Key) {
            case Key.Escape:
                if (vm.IsSearching) {
                    vm.CancelDeferredSearch();
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
                if (vm.SelectedResult?.OnActivate is { } action)
                {
                    action();
                    vm.SearchText = "";
                    Hide();
                }
                e.Handled = true;
                break;

            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                (Application.Current as App)?.OpenSettings();
                e.Handled = true;
                break;
        }
    }

    private static void SelectNext(MainWindowViewModel vm, int delta) {
        if (vm.Results.Count == 0) return;

        var current = vm.SelectedResult is null ? -1 : vm.Results.IndexOf(vm.SelectedResult);
        var next = (current + delta + vm.Results.Count) % vm.Results.Count;
        vm.SelectedResult = vm.Results[next];
    }
}

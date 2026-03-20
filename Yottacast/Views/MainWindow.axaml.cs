using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yottacast.ViewModels;
using Yottacast;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        Opened += (_, _) => SearchBox.Focus();
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
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        switch (e.Key) {
            // Consume ALT+Space so macOS doesn't produce a beep for the unhandled key
            case Key.Space when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                e.Handled = true;
                break;

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

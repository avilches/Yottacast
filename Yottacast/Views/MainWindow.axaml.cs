using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
            } else if (DataContext is MainWindowViewModel vm) {
                var trimmed = vm.SearchText.Trim();
                Console.WriteLine($"[Window] Hiding - SearchText='{vm.SearchText}' trimmed='{trimmed}'");
                vm.SearchText = trimmed;
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
                if (string.IsNullOrEmpty(vm.SearchText)) {
                    Hide();
                } else {
                    vm.SearchText = "";
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

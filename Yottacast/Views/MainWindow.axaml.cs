using Avalonia.Controls;
using Avalonia.Input;
using Yottacast.ViewModels;

namespace Yottacast.Views;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        Opened += (_, _) => SearchBox.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm is null) return;

        switch (e.Key) {
            case Key.Escape:
                vm.SearchText = "";
                Hide();
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
                // TODO: execute selected result
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
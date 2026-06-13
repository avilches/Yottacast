using Avalonia.Controls;
using Avalonia.Interactivity;
using Yottacast.ViewModels;

namespace Yottacast.Views.Settings;

public partial class SettingsFileEditorView : UserControl {
    public SettingsFileEditorView() {
        InitializeComponent();
    }

    private void OnAddExtensionClick(object? sender, RoutedEventArgs e) {
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (this.FindControl<TextBox>("NewExtensionBox") is { } box && !string.IsNullOrWhiteSpace(box.Text)) {
            vm.AddFileEditorExtension(box.Text);
            box.Text = "";
        }
    }

    private void OnRemoveExtensionClick(object? sender, RoutedEventArgs e) {
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (sender is Button { DataContext: string ext })
            vm.RemoveFileEditorExtension(ext);
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using Yottacast.ViewModels;

namespace Yottacast.Views;

public partial class SettingsWindow : Window {
    public SettingsWindow() {
        InitializeComponent();
    }

    // Click on the hotkey border → start capture; e.Handled stops bubbling to the window handler below
    private void OnHotkeyAreaPointerPressed(object? sender, PointerPressedEventArgs e) {
        (DataContext as SettingsWindowViewModel)?.StartHotkeyCapture();
        e.Handled = true;
    }

    // Click anywhere else in the window → cancel capture
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm)
            vm.CancelHotkeyCapture();
        base.OnPointerPressed(e);
    }

    // ── Folder picker ─────────────────────────────────────────────────────────
    private async Task<string?> PickFolderAsync() {
        var results = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select Folder", AllowMultiple = false });
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    private async void OnAddSearchFolderClicked(object? sender, RoutedEventArgs e) {
        var path = await PickFolderAsync();
        if (path != null && DataContext is SettingsWindowViewModel vm)
            vm.AddSearchFolder(path);
    }

    private async void OnAddAppDirectoryClicked(object? sender, RoutedEventArgs e) {
        var path = await PickFolderAsync();
        if (path != null && DataContext is SettingsWindowViewModel vm)
            vm.AddAppDirectory(path);
    }

    private void OnRemoveSearchFolderClicked(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string path } && DataContext is SettingsWindowViewModel vm)
            vm.RemoveSearchFolder(path);
    }

    private void OnRemoveAppDirectoryClicked(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string path } && DataContext is SettingsWindowViewModel vm)
            vm.RemoveAppDirectory(path);
    }
}

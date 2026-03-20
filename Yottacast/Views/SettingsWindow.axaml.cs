using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using Yottacast.Services;
using Yottacast.ViewModels;

namespace Yottacast.Views;

public partial class SettingsWindow : Window {
    public SettingsWindow() {
        InitializeComponent();
    }

    // Intercept keyboard input while capturing a new hotkey combination
    protected override void OnKeyDown(KeyEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm) {
            vm.ProcessKeyCapture(e.Key, e.KeyModifiers);
            e.Handled = true;
            return;
        }
        var (closeMods, closeKey) = AppHandler.Instance.CloseWindowShortcut;
        if (e.Key == closeKey && e.KeyModifiers == closeMods) {
            Hide();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    // Cancel any native close (e.g. macOS performClose: from NSMenu) and hide instead.
    // SettingsWindow is kept alive in memory so no "last window closed" event is ever fired.
    protected override void OnClosing(WindowClosingEventArgs e) {
        e.Cancel = true;
        Hide();
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

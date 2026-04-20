using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System.Linq;
using System.Threading.Tasks;
using Yottacast.Services;
using Yottacast.ViewModels;

namespace Yottacast.Views;

public partial class SettingsWindow : Window {
    public SettingsWindow() {
        InitializeComponent();

        // Fijar el ThemeVariant según el OS, ignorando el tema del buscador.
        // Window.RequestedThemeVariant prevalece sobre Application.RequestedThemeVariant.
        var osTheme = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
        RequestedThemeVariant = osTheme == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        // Inyectar colores y fuente nativos del OS (definidos en AppHandler de cada plataforma).
        AppHandler.Instance.ApplySettingsTheme(this);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm) {
            vm.UpdateCapturingModifiers(e.KeyModifiers);
            if (!IsModifierKey(e.Key))
                vm.ProcessKeyCapture(e.Key, e.KeyModifiers);
            e.Handled = true;
            return;
        }
        var (closeMods, closeKey) = AppHandler.Instance.CloseWindowShortcut;
        if (e.Key == closeKey && e.KeyModifiers == closeMods) {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm && IsModifierKey(e.Key)) {
            vm.UpdateCapturingModifiers(e.KeyModifiers);
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
          or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    // Click on the hotkey border → start capture only if not already capturing
    // (prevents restarting when the cancel button inside is clicked)
    private void OnHotkeyAreaPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: false } vm)
            vm.StartHotkeyCapture();
        e.Handled = true;  // always stop bubble so OnPointerPressed doesn't also cancel
    }

    private void OnCancelHotkeyCaptureClicked(object? sender, RoutedEventArgs e) {
        (DataContext as SettingsWindowViewModel)?.CancelHotkeyCapture();
    }

    // Click anywhere else in the window → cancel capture
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm)
            vm.CancelHotkeyCapture();
        base.OnPointerPressed(e);
    }

    // ── Engine prefix inline editing ──────────────────────────────────────────
    private void OnPrefixDoubleTapped(object? sender, TappedEventArgs e) {
        if (sender is not TextBlock { DataContext: WebSearchEngineRowViewModel vm } tb) return;
        if (!vm.IsPrefixEnabled) return;

        vm.IsPrefixEditing = true;

        // Focus the TextBox sibling after Avalonia updates IsVisible in the next layout pass
        if (tb.Parent is Panel panel) {
            var textBox = panel.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox != null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    textBox.Focus();
                    textBox.SelectAll();
                });
        }
    }

    private void OnPrefixLostFocus(object? sender, RoutedEventArgs e) {
        if (sender is TextBox { DataContext: WebSearchEngineRowViewModel vm })
            vm.IsPrefixEditing = false;
    }

    private void OnPrefixKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key is Key.Enter or Key.Escape &&
            sender is TextBox { DataContext: WebSearchEngineRowViewModel vm }) {
            vm.IsPrefixEditing = false;
            e.Handled = true;
        }
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

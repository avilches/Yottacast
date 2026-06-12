using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using System.Threading.Tasks;
using Yottacast.Services;
using Yottacast.ViewModels;

namespace Yottacast.Views;

public partial class SettingsWindow : Window {
    private bool _wasDeactivated;

    public SettingsWindow() {
        InitializeComponent();

        // Re-check permissions when the user comes back from the System Settings panel.
        // We only react to focus regain after a real loss to avoid double-refresh on open.
        Deactivated += (_, _) => _wasDeactivated = true;
        Activated   += (_, _) => {
            if (!_wasDeactivated) return;
            _wasDeactivated = false;
            if (DataContext is SettingsWindowViewModel { IsPermissionsSelected: true } vm)
                vm.RefreshPermissions();
        };

        // Fijar el ThemeVariant según el OS, ignorando el tema del buscador.
        // Window.RequestedThemeVariant prevalece sobre Application.RequestedThemeVariant.
        var osTheme = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
        RequestedThemeVariant = osTheme == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        // Inyectar colores y fuente nativos del OS (definidos en AppHandler de cada plataforma).
        AppHandler.Instance.ApplySettingsTheme(this);

        // El handle nativo solo está disponible tras Show(); deshabilitamos el botón en Opened.
        Opened += (_, _) => AppHandler.Instance.DisableMinimizeButton(this);
        Closed += (_, _) => (DataContext as SettingsWindowViewModel)?.OnWindowClosed();

        // Bloquear entrada de letras en los campos numéricos; usamos Tunnel para interceptar antes del TextBox interior
        DecimalPlacesInput.AddHandler(InputElement.TextInputEvent, OnDecimalPlacesTextInputting, RoutingStrategies.Tunnel);
        ExchangeRateRefreshInput.AddHandler(InputElement.TextInputEvent, OnDecimalPlacesTextInputting, RoutingStrategies.Tunnel);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (DataContext is SettingsWindowViewModel vm2 && vm2.IsCapturingClipboardHotkey) {
            vm2.UpdateCapturingClipboardModifiers(e.KeyModifiers);
            if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                              or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin))
                vm2.ProcessClipboardKeyCapture(e.Key, e.KeyModifiers);
            e.Handled = true;
            return;
        }
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
        if (DataContext is SettingsWindowViewModel vm3 && vm3.IsCapturingClipboardHotkey && IsModifierKey(e.Key)) {
            vm3.UpdateCapturingClipboardModifiers(e.KeyModifiers);
            e.Handled = true;
            return;
        }
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
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: false } vm) {
            // Clear focus from any control (e.g. Theme combo) so it doesn't intercept keys during capture
            FocusManager?.ClearFocus();
            vm.StartHotkeyCapture();
        }
        e.Handled = true;  // always stop bubble so OnPointerPressed doesn't also cancel
    }

    private void OnCancelHotkeyCaptureClicked(object? sender, RoutedEventArgs e) {
        (DataContext as SettingsWindowViewModel)?.CancelHotkeyCapture();
    }

    private void OnClipboardHotkeyPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel vm) {
            FocusManager?.ClearFocus();
            vm.StartClipboardHotkeyCapture();
        }
        e.Handled = true;
    }

    // Click anywhere else in the window → cancel capture
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: true } vm)
            vm.CancelHotkeyCapture();
        if (DataContext is SettingsWindowViewModel { IsCapturingClipboardHotkey: true } vm2)
            vm2.CancelClipboardHotkeyCapture();
        base.OnPointerPressed(e);
    }

    // ── Engine URL flyout ─────────────────────────────────────────────────────
    private void OnFlyoutInputKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key != Key.Enter || sender is not Control ctrl) return;
        var popup = ctrl.GetVisualAncestors().OfType<Popup>().FirstOrDefault();
        if (popup != null) {
            popup.IsOpen = false;
            e.Handled = true;
        }
    }

    // Cuando el TextBox de URL pierde el foco: si está vacío, restaura el valor por defecto
    // para que la próxima apertura del popup muestre la URL editable.
    // Post() garantiza que ResetUrlCommand (si fue el X quien quitó el foco) ya se ejecutó.
    private void OnFlyoutUrlLostFocus(object? sender, RoutedEventArgs e) {
        if (sender is TextBox { DataContext: WebSearchEngineRowViewModel vm })
            Avalonia.Threading.Dispatcher.UIThread.Post(vm.NormalizeQueryUrl);
    }

    // Command binding already calls ResetUrlCommand; this handler only sets focus on the URL TextBox.
    private void OnResetUrlClicked(object? sender, RoutedEventArgs e) {
        if (sender is Button { Parent: Grid grid }) {
            var urlBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (urlBox != null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => urlBox.Focus());
        }
    }

    private void OnEngineFlyoutOpened(object? sender, EventArgs e) {
        // Disable light-dismiss so clicking Test URL / Show folder / Edit source
        // (which open external apps and deactivate this window) does not close the flyout.
        // The flyout can only be closed via the explicit "Close" button.
        if (sender is not Flyout flyout) return;
        // Access the protected Popup property via reflection
        var popupProp = typeof(PopupFlyoutBase).GetProperty("Popup",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (popupProp?.GetValue(flyout) is Popup popup)
            popup.IsLightDismissEnabled = false;
    }

    private void OnFlyoutCloseClicked(object? sender, RoutedEventArgs e) {
        if (sender is Control ctrl) {
            var presenter = ctrl.GetVisualAncestors().OfType<FlyoutPresenter>().FirstOrDefault();
            if (presenter?.Parent is Popup popup)
                popup.IsOpen = false;
        }
    }

    private async void OnTestUrlClicked(object? sender, RoutedEventArgs e) {
        // Give the browser time to launch, then bring settings back to front
        await Task.Delay(500);
        Activate();
    }

    private async void OnPluginActionClicked(object? sender, RoutedEventArgs e) {
        // Re-activate settings after the OS opens a folder/file externally
        await Task.Delay(500);
        Activate();
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
        if (sender is Button { Tag: SearchFolderItem item } && DataContext is SettingsWindowViewModel vm)
            vm.RemoveSearchFolder(item);
    }

    private void OnAddCommonFoldersClicked(object? sender, RoutedEventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            vm.AddCommonFolders();
    }

    private void OnRemoveAppDirectoryClicked(object? sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string path } && DataContext is SettingsWindowViewModel vm)
            vm.RemoveAppDirectory(path);
    }

    private void OnAddCommonAppDirectoriesClicked(object? sender, RoutedEventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            vm.AddCommonAppDirectories();
    }

    private void OnDecimalPlacesTextInputting(object? sender, TextInputEventArgs e) {
        if (e.Text != null && !e.Text.All(char.IsDigit))
            e.Handled = true;
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

    private async void OnBrowserDropDownOpened(object? sender, EventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            await vm.RefreshBrowsersAsync();
    }

    private async void OnTerminalDropDownOpened(object? sender, EventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            await vm.RefreshTerminalsAsync();
    }
}

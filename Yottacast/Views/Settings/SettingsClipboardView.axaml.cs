using Avalonia.Controls;
using Avalonia.Input;
using Yottacast.ViewModels;

namespace Yottacast.Views.Settings;

public partial class SettingsClipboardView : UserControl {
    public SettingsClipboardView() {
        InitializeComponent();
    }

    // Click on the clipboard hotkey border -> start capture only if not already capturing
    // (prevents restarting when the cancel button inside is clicked)
    private void OnClipboardHotkeyPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingClipboardHotkey: false } vm) {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            vm.StartClipboardHotkeyCapture();
        }
        e.Handled = true;
    }
}

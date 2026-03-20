using Avalonia.Controls;
using Avalonia.Input;
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
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
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
}

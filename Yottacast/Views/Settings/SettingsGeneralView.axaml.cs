using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yottacast.ViewModels;

namespace Yottacast.Views.Settings;

public partial class SettingsGeneralView : UserControl {
    public SettingsGeneralView() {
        InitializeComponent();
    }

    private void OnHotkeyAreaPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is SettingsWindowViewModel { IsCapturingHotkey: false } vm) {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            vm.StartHotkeyCapture();
        }
        e.Handled = true;
    }

    private async void OnTerminalDropDownOpened(object? sender, EventArgs e) {
        if (DataContext is SettingsWindowViewModel vm)
            await vm.RefreshTerminalsAsync();
    }
}

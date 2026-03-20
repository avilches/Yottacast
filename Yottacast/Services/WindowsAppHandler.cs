using Avalonia.Input;

namespace Yottacast.Services;

internal sealed class WindowsAppHandler : AppHandler {
    public override void OnFrameworkInitializationCompleted() { }
    public override void OnShow() { }
    public override void OnHide() { }
    public override (KeyModifiers Modifiers, Key Key) CloseWindowShortcut => (KeyModifiers.Control, Key.F4);
}
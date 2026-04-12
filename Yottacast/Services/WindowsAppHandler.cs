using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Input;

namespace Yottacast.Services;

internal sealed class WindowsAppHandler : AppHandler {
    public override void OnFrameworkInitializationCompleted() { }
    public override void OnShow() { }
    public override void OnHide() { }
    public override (KeyModifiers Modifiers, Key Key) CloseWindowShortcut => (KeyModifiers.Control, Key.F4);

    public override async Task SimulatePasteAsync() {
        await Task.Delay(150);
        const byte VK_CONTROL = 0x11;
        const byte VK_V = 0x56;
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_V, 0, 0, 0);
        keybd_event(VK_V, 0, 2, 0);       // KEYEVENTF_KEYUP
        keybd_event(VK_CONTROL, 0, 2, 0); // KEYEVENTF_KEYUP
    }

    public override void HideCursor() => ShowCursorWin(false);
    public override void ShowCursor() => ShowCursorWin(true);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern int ShowCursorWin(bool show);
}
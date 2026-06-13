using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Yottacast.Core;

namespace Yottacast.Services;

internal sealed class WindowsAppHandler : AppHandler {
    public override void OnFrameworkInitializationCompleted() { }
    public override void OnHide() { }
    public override (KeyModifiers Modifiers, Key Key) CloseWindowShortcut => (KeyModifiers.Control, Key.F4);

    public override string MetaSymbol => "⊞";

    public override IReadOnlyList<(KeyModifiers, Key)> ForbiddenHotkeys =>
        [(KeyModifiers.Control, Key.F4), (KeyModifiers.Alt, Key.F4)];

    public override async Task SimulatePasteAsync() {
        await Task.Delay(AppDefaults.PasteDelayMs);
        const byte VK_CONTROL = 0x11;
        const byte VK_V = 0x56;
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_V, 0, 0, 0);
        keybd_event(VK_V, 0, 2, 0);       // KEYEVENTF_KEYUP
        keybd_event(VK_CONTROL, 0, 2, 0); // KEYEVENTF_KEYUP
    }

    public override void HideCursor() => ShowCursorWin(false);
    public override void ShowCursor() => ShowCursorWin(true);

    public override PixelPoint? GetMousePosition() {
        if (!GetCursorPos(out var pt)) return null;
        return new PixelPoint(pt.X, pt.Y);
    }

    // Windows Settings theme — Fluent/WinUI-inspired palette (placeholder, matches macOS for now).
    // Font: Segoe UI (system font on Windows).
    public override void ApplySettingsTheme(Window window) {
        window.FontFamily = new Avalonia.Media.FontFamily("Segoe UI");
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = MakeThemeDict(
            ("Theme.Window.Background",            Brush("#F3F3F3")),
            ("Theme.Results.Icon.Background",      Brush("#E8E8E8")),
            ("Theme.Divider.Color",                Brush("#E0E0E0")),
            ("Theme.Results.Title.Color",          Brush("#1C1C1C")),
            ("Theme.Results.Subtitle.Color",       Brush("#616161")),
            ("Theme.Results.Category.Color",       Brush("#8A8A8A")),
            ("Theme.Results.Selection.Background", Brush("#0078D4")),
            ("Theme.Results.Selection.Color",      Brush("#FFFFFF")),
            ("Theme.Results.Hover.Background",     Brush("#12000000")),
            ("Theme.Footer.Color",                 Brush("#8A8A8A")),
            ("Theme.Search.Caret",                 Brush("#0078D4")),
            ("Theme.Results.Title.Size",           13d),
            ("Theme.Results.Category.Size",        11d),
            ("Theme.NoResults.Title.Size",         14d)
        );
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = MakeThemeDict(
            ("Theme.Window.Background",            Brush("#202020")),
            ("Theme.Results.Icon.Background",      Brush("#2C2C2C")),
            ("Theme.Divider.Color",                Brush("#3A3A3A")),
            ("Theme.Results.Title.Color",          Brush("#FFFFFF")),
            ("Theme.Results.Subtitle.Color",       Brush("#ABABAB")),
            ("Theme.Results.Category.Color",       Brush("#6B6B6B")),
            ("Theme.Results.Selection.Background", Brush("#0078D4")),
            ("Theme.Results.Selection.Color",      Brush("#FFFFFF")),
            ("Theme.Results.Hover.Background",     Brush("#18FFFFFF")),
            ("Theme.Footer.Color",                 Brush("#6B6B6B")),
            ("Theme.Search.Caret",                 Brush("#4CC2FF")),
            ("Theme.Results.Title.Size",           13d),
            ("Theme.Results.Category.Size",        11d),
            ("Theme.NoResults.Title.Size",         14d)
        );
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", EntryPoint = "ShowCursor")]
    private static extern int ShowCursorWin(bool show);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
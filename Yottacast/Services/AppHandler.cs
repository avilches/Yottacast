using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Yottacast.Core.Platform;

namespace Yottacast.Services;

internal abstract class AppHandler {
    public static readonly AppHandler Instance =
        OperatingSystem.IsMacOS()   ? new MacAppHandler()     :
        OperatingSystem.IsWindows() ? new WindowsAppHandler() :
                                      new LinuxAppHandler();

    public abstract void OnFrameworkInitializationCompleted();
    public abstract void OnShow();
    public abstract void OnHide();

    /// <summary>
    /// Platform-specific "close window" shortcut: Cmd+W on macOS, Ctrl+F4 on Windows, Ctrl+W on Linux.
    /// Used by SettingsWindow to close itself, and by MainWindow to consume the shortcut and prevent accidental close.
    /// </summary>
    public abstract (KeyModifiers Modifiers, Key Key) CloseWindowShortcut { get; }

    /// <summary>
    /// Platform-specific "quit app" shortcut: Cmd+Q on macOS, null on other platforms.
    /// </summary>
    public virtual (KeyModifiers Modifiers, Key Key)? QuitShortcut => null;

    /// <summary>Visual symbol for each modifier key, localised per OS.</summary>
    public virtual string CtrlSymbol  => "Ctrl";
    public virtual string AltSymbol   => "Alt";
    public virtual string ShiftSymbol => "⇧";
    public virtual string MetaSymbol  => "Meta";

    /// <summary>
    /// Hotkey combinations that must not be used as the global hotkey on this platform.
    /// Expressed as Avalonia (KeyModifiers, Key) pairs.
    /// </summary>
    public virtual IReadOnlyList<(KeyModifiers Modifiers, Key Key)> ForbiddenHotkeys =>
        Array.Empty<(KeyModifiers, Key)>();

    /// <summary>
    /// Returns true if <paramref name="config"/> matches any entry in <see cref="ForbiddenHotkeys"/>.
    /// Key name comparison is case-insensitive to handle hand-edited JSON.
    /// </summary>
    public bool IsForbidden(HotkeyConfig config) {
        foreach (var (mods, key) in ForbiddenHotkeys) {
            if (mods.HasFlag(KeyModifiers.Alt)     != config.Alt)     continue;
            if (mods.HasFlag(KeyModifiers.Control) != config.Ctrl)    continue;
            if (mods.HasFlag(KeyModifiers.Shift)   != config.Shift)   continue;
            if (mods.HasFlag(KeyModifiers.Meta)    != config.Meta)    continue;
            if (!string.Equals(key.ToString(), config.KeyName, StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Waits briefly for the previous app to activate, then simulates a paste shortcut (Cmd+V / Ctrl+V).
    /// Called after OnHide() when a result with PasteAfterActivate=true is confirmed.
    /// </summary>
    public virtual Task SimulatePasteAsync() => Task.CompletedTask;

    /// <summary>Hides the system mouse cursor.</summary>
    public virtual void HideCursor() { }

    /// <summary>Restores the system mouse cursor.</summary>
    public virtual void ShowCursor() { }

    /// <summary>Returns the current mouse cursor position in global screen coordinates (top-left origin), or null if unavailable.</summary>
    public virtual PixelPoint? GetMousePosition() => null;

    /// <summary>
    /// Injects OS-native colors and font into the Settings window.
    /// Each platform defines its own Light/Dark palette independently.
    /// Called from SettingsWindow constructor after InitializeComponent().
    /// </summary>
    public abstract void ApplySettingsTheme(Window window);

    /// <summary>Builds a theme-variant resource dictionary for Settings from named color/double pairs.</summary>
    protected static ResourceDictionary MakeThemeDict(params (string Key, object Value)[] entries) {
        var d = new ResourceDictionary();
        foreach (var (key, value) in entries)
            d[key] = value;
        return d;
    }

    protected static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
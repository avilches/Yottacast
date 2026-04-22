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

    /// <summary>
    /// Shows the window using platform-appropriate activation behavior.
    /// On macOS: shows the window, then re-activates the previous app so its traffic lights
    /// stay colored, then makes the Yottacast window the key window via makeKeyAndOrderFront:.
    /// On other platforms: calls window.Show() and window.Activate() (default behavior).
    /// </summary>
    public virtual void ShowWindow(Window window) {
        window.Show();
        window.Activate();
    }

    public abstract void OnHide();

    /// <summary>
    /// Brings an already-visible window to the foreground and gives it keyboard focus,
    /// without modifying the saved previous-app reference used for focus restoration on hide.
    /// Used in sticky mode when the hotkey is pressed while the window is visible but unfocused.
    /// Default implementation calls Activate(); macOS overrides with activateIgnoringOtherApps + makeKeyAndOrderFront:.
    /// </summary>
    public virtual void FocusWindow(Window window) {
        window.Activate();
    }

    /// <summary>
    /// Returns true if this app's window is currently in the foreground (has keyboard focus / is key window).
    /// Default uses Avalonia's window.IsActive; macOS overrides with isKeyWindow check on the NSWindow.
    /// </summary>
    public virtual bool IsWindowFocused(Window window) => window.IsActive;

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

    /// <summary>Disables the minimize button on the native window chrome. No-op on platforms where this is not applicable.</summary>
    public virtual void DisableMinimizeButton(Window window) { }

    /// <summary>Shows the app icon in the Dock/taskbar. No-op on platforms where this is not applicable.</summary>
    public virtual void ShowDockIcon() { }

    /// <summary>Hides the app icon from the Dock/taskbar. No-op on platforms where this is not applicable.</summary>
    public virtual void HideDockIcon() { }

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
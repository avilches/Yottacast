using System;
using Avalonia.Input;

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
}
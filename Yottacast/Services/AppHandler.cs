using System;

namespace Yottacast.Services;

internal abstract class AppHandler {
    public static readonly AppHandler Instance =
        OperatingSystem.IsMacOS()   ? new MacAppHandler()     :
        OperatingSystem.IsWindows() ? new WindowsAppHandler() :
                                      new LinuxAppHandler();

    public abstract void OnFrameworkInitializationCompleted();
    public abstract void OnShow();
    public abstract void OnHide();
}
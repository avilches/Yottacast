using Avalonia;
using System;
using Yottacast.Services;

namespace Yottacast;

sealed class Program {
    [STAThread]
    public static void Main(string[] args) {
        // OnStart must run before BuildAvaloniaApp() so the platform can configure itself
        // before Avalonia initializes (e.g. hide Dock icon on macOS before NSApplication starts).
        AppHandler.Instance.OnStart();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
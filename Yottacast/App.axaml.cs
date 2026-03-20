using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SharpHook;
using SharpHook.Data;
using Yottacast.Core.Platform;
using Yottacast.Core.Process;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast.Views;

namespace Yottacast;

public partial class App : Application {
    private IGlobalHook? _globalHook;
    private SettingsWindow? _settingsWindow;
    private IServiceProvider _services = null!;
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            AppHandler.Instance.OnFrameworkInitializationCompleted();
            _services = BuildServices();

            var userSettings = _services.GetRequiredService<UserSettings>();
            var themeService = _services.GetRequiredService<ThemeService>();
            themeService.Apply(userSettings.Theme);

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindow = new MainWindow {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.MainWindow = mainWindow;

            // Wire up clipboard so Core code can copy results without depending on Avalonia
            var clipboardService = _services.GetRequiredService<ClipboardService>();
            clipboardService.Initialize(text =>
                Dispatcher.UIThread.InvokeAsync(() => {
                    var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
                    if (clipboard != null) _ = clipboard.SetTextAsync(text);
                }));

            var globalSearch = _services.GetRequiredService<GlobalSearch>();
            desktop.Exit += async (_, _) => await globalSearch.Stop();
            RegisterGlobalHotKey(desktop);

            base.OnFrameworkInitializationCompleted();

            AppHandler.Instance.OnShow();
            desktop.MainWindow.Show();
            desktop.MainWindow.Activate();

            globalSearch.Start();
            _ = _services.GetRequiredService<MathJsEngine>(); // warm up in background
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public async void OpenSettings() {
        if (_settingsWindow is { IsVisible: true }) {
            _settingsWindow.Activate();
            return;
        }
        var appSearch = _services.GetRequiredService<ApplicationSearch>();
        await appSearch.WhenReady();
        _settingsWindow = new SettingsWindow {
            DataContext = _services.GetRequiredService<SettingsWindowViewModel>(),
        };
        _settingsWindow.Show();
    }

    private static IServiceProvider BuildServices() {
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                ComputeLogPath(),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(serilogLogger, dispose: true));

        services.AddSingleton<StandardCommandRunner>();
        services.AddSingleton<PlatformProvider>(sp =>
            OperatingSystem.IsMacOS()
                ? new MacOsPlatformProvider(sp.GetRequiredService<ILogger<MacOsPlatformProvider>>())
                : OperatingSystem.IsWindows()
                    ? new WindowsPlatformProvider(sp.GetRequiredService<StandardCommandRunner>(), sp.GetRequiredService<ILogger<WindowsPlatformProvider>>())
                    : new LinuxPlatformProvider(sp.GetRequiredService<StandardCommandRunner>(), sp.GetRequiredService<ILogger<LinuxPlatformProvider>>()));

        services.AddSingleton(sp => UserSettings.Load(
            sp.GetRequiredService<PlatformProvider>(),
            sp.GetRequiredService<ILogger<UserSettings>>()));

        services.AddSingleton<ThemeService>();
        services.AddSingleton<ApplicationSearch>();
        services.AddSingleton<BrowserDiscovery>();
        services.AddSingleton<TerminalDiscovery>();
        services.AddSingleton<FileSearch>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<MathJsEngine>();
        services.AddSingleton<CalculatorSearch>();

        // Register ISearchSource implementations.
        services.AddSingleton<UserDocumentSearch>();
        services.AddSingleton<RandomSearch>();
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<ApplicationSearch>());
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<UserDocumentSearch>());
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<CalculatorSearch>());
        // services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<RandomSearch>());

        services.AddSingleton<GlobalSearch>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static string ComputeLogPath() {
        var dir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Yottacast")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yottacast", "Logs");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "yottacast-.log");
    }

    private void RegisterGlobalHotKey(IClassicDesktopStyleApplicationLifetime desktop) {
        // SimpleGlobalHook runs handlers synchronously on the hook thread, which is required
        // for e.SuppressEvent = true to work. TaskPoolGlobalHook runs handlers on other threads
        // where suppression has no effect.
        _globalHook = new SimpleGlobalHook();
        _globalHook.KeyPressed += (_, e) => {
            var isAlt = e.RawEvent.Mask.HasFlag(EventMask.LeftAlt) ||
                        e.RawEvent.Mask.HasFlag(EventMask.RightAlt);
            if (e.Data.KeyCode == KeyCode.VcSpace && isAlt) {
                // Suppress the event at OS level so it is not delivered to any app.
                // This prevents beeps in both Yottacast and the previously focused app.
                // Requires Accessibility permission on macOS; silently ignored without it.
                e.SuppressEvent = true;
                Dispatcher.UIThread.InvokeAsync(() => {
                    var window = desktop.MainWindow;
                    if (window is null) return;
                    if (window.IsVisible && window.IsActive) {
                        window.Hide();
                        AppHandler.Instance.OnHide();
                    } else {
                        AppHandler.Instance.OnShow();
                        window.Show();
                        window.Activate();
                    }
                });
            }
        };
        _ = _globalHook.RunAsync();
    }

    private void DisableAvaloniaDataAnnotationValidation() {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove) {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

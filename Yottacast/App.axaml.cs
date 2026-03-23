using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SharpHook;
using SharpHook.Data;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Search.UserDocuments;
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

            var updateChecker = _services.GetRequiredService<UpdateChecker>();
            RunMigrations(userSettings, updateChecker, _services.GetRequiredService<ILogger<App>>());

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindowViewModel = _services.GetRequiredService<MainWindowViewModel>();
            mainWindowViewModel.Initialize();
            var mainWindow = new MainWindow { DataContext = mainWindowViewModel };
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

            globalSearch.Start();
            _ = ShowWhenInstantReadyAsync(globalSearch, desktop);
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowWhenInstantReadyAsync(GlobalSearch globalSearch, IClassicDesktopStyleApplicationLifetime desktop) {
        // Block view until all instant search are ready
        await globalSearch.WhenInstantReady();
        await Dispatcher.UIThread.InvokeAsync(() => {
            AppHandler.Instance.OnShow();
            desktop.MainWindow?.Show();
            desktop.MainWindow?.Activate();
        });
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

    private static void RunMigrations(UserSettings settings, UpdateChecker updateChecker, Microsoft.Extensions.Logging.ILogger logger) {
        var current = updateChecker.CurrentVersion;
        if (settings.LastLaunchedVersion == current) return;

        logger.LogInformation("Version changed: '{Prev}' → '{Current}' — running migrations",
            settings.LastLaunchedVersion, current);

        // Añadir migraciones específicas aquí según evolucione el app.
        // Ejemplo: if (string.IsNullOrEmpty(settings.LastLaunchedVersion)) { /* primera vez */ }

        settings.LastLaunchedVersion = current;
        settings.Save();
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

        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<PlatformProvider>(sp =>
            OperatingSystem.IsMacOS()
                ? new MacOsPlatformProvider(sp.GetRequiredService<ILogger<MacOsPlatformProvider>>())
                : OperatingSystem.IsWindows()
                    ? new WindowsPlatformProvider(sp.GetRequiredService<ProcessRunner>(), sp.GetRequiredService<ILogger<WindowsPlatformProvider>>())
                    : new LinuxPlatformProvider(sp.GetRequiredService<ProcessRunner>(), sp.GetRequiredService<ILogger<LinuxPlatformProvider>>()));

        services.AddSingleton(sp => UserSettings.Load(
            sp.GetRequiredService<PlatformProvider>(),
            sp.GetRequiredService<ILogger<UserSettings>>()));

        services.AddSingleton<ThemeService>();
        services.AddSingleton<ApplicationSearch>();
        services.AddSingleton<BrowserDiscovery>();
        services.AddSingleton<TerminalDiscovery>();
        services.AddSingleton<FileSearch>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<ICurrencyRateProvider, StaticCurrencyRateProvider>();
        services.AddSingleton<MathJsEngine>();
        services.AddSingleton<CalculatorSearch>();
        services.AddSingleton<EmojiDataLoader>();
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
            sp.GetRequiredService<ClipboardService>(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yottacast"),
            sp.GetRequiredService<EmojiDataLoader>(),
            sp.GetRequiredService<ILogger<EmojiSearch>>()));

        // Register IInstantSearchSource and IDeferredSearchSource implementations.
        services.AddSingleton<UserDocumentSearch>();
        services.AddSingleton<RandomSearch>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ApplicationSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<CalculatorSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<EmojiSearch>());
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<UserDocumentSearch>());
        // services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<RandomSearch>());

        services.AddSingleton<GlobalSearch>();
        services.AddSingleton<UpdateChecker>();

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

    private static readonly Dictionary<string, KeyCode> KeyNameMap = BuildKeyNameMap();

    private static Dictionary<string, KeyCode> BuildKeyNameMap() {
        var map = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase) {
            ["Space"] = KeyCode.VcSpace,
            ["Enter"] = KeyCode.VcEnter,
            ["Tab"]   = KeyCode.VcTab,
            ["Backspace"] = KeyCode.VcBackspace,
            ["Delete"] = KeyCode.VcDelete,
            ["Escape"] = KeyCode.VcEscape,
        };
        // A–Z
        var aCode = (int)KeyCode.VcA;
        for (int i = 0; i < 26; i++)
            map[((char)('A' + i)).ToString()] = (KeyCode)(aCode + i);
        // 0–9
        var zeroCode = (int)KeyCode.Vc0;
        for (int i = 0; i < 10; i++)
            map[i.ToString()] = (KeyCode)(zeroCode + i);
        // F1–F12
        var f1Code = (int)KeyCode.VcF1;
        for (int i = 0; i < 12; i++)
            map[$"F{i + 1}"] = (KeyCode)(f1Code + i);
        return map;
    }

    private static KeyCode KeyNameToKeyCode(string name) =>
        KeyNameMap.TryGetValue(name, out var code) ? code : KeyCode.VcUndefined;

    private void RegisterGlobalHotKey(IClassicDesktopStyleApplicationLifetime desktop) {
        // SimpleGlobalHook runs handlers synchronously on the hook thread, which is required
        // for e.SuppressEvent = true to work. TaskPoolGlobalHook runs handlers on other threads
        // where suppression has no effect.
        var settings = _services.GetRequiredService<UserSettings>();
        _globalHook = new SimpleGlobalHook();
        _globalHook.KeyPressed += (_, e) => {

            var mask   = e.RawEvent.Mask;
            var hasAlt   = mask.HasFlag(EventMask.LeftAlt)   || mask.HasFlag(EventMask.RightAlt);
            var hasCtrl  = mask.HasFlag(EventMask.LeftCtrl)  || mask.HasFlag(EventMask.RightCtrl);
            var hasShift = mask.HasFlag(EventMask.LeftShift) || mask.HasFlag(EventMask.RightShift);
            var hasMeta  = mask.HasFlag(EventMask.LeftMeta)  || mask.HasFlag(EventMask.RightMeta);

            var hotkey = settings.ParsedHotkey;


            if (e.Data.KeyCode == KeyNameToKeyCode(hotkey.KeyName)
                && hasAlt == hotkey.Alt && hasCtrl == hotkey.Ctrl
                && hasShift == hotkey.Shift && hasMeta == hotkey.Meta) {
                // Suppress the event at OS level so it is not delivered to any app.
                // This prevents beeps in both Yottacast and the previously focused app.
                // Requires Accessibility permission on macOS; silently ignored without it.
                e.SuppressEvent = true;
                Dispatcher.UIThread.InvokeAsync(() => {
                    var window = desktop.MainWindow;
                    if (window is null) return;
                    if (window is { IsVisible: true, IsActive: true }) {
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

    private static void DisableAvaloniaDataAnnotationValidation() {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove) {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

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
using Yottacast.Core;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Search.Dictionary;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast.Views;

namespace Yottacast;

public partial class App : Application {
    private IGlobalHook? _globalHook;
    private SettingsWindow? _settingsWindow;
    private SettingsWindowViewModel? _settingsVm;
    private IServiceProvider _services = null!;
    private volatile bool _isToggling = false;
    private volatile bool _hotkeyDown = false;

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            AppHandler.Instance.OnFrameworkInitializationCompleted();
            _services = BuildServices();

            var userSettings = _services.GetRequiredService<UserSettings>();
            var pluginService = _services.GetRequiredService<PluginService>();
            var themeService = _services.GetRequiredService<ThemeService>();
            themeService.Apply(userSettings.Theme);
            themeService.StartWatching(pluginService);

            var updateChecker = _services.GetRequiredService<UpdateChecker>();
            RunMigrations(userSettings, updateChecker, _services.GetRequiredService<ILogger<App>>());

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindowViewModel = _services.GetRequiredService<MainWindowViewModel>();
            mainWindowViewModel.Initialize();
            var mainWindow = new MainWindow(userSettings, _services.GetRequiredService<ILogger<MainWindow>>()) { DataContext = mainWindowViewModel };
            desktop.MainWindow = mainWindow;

            // Auto-hide when losing focus in non-sticky mode (Alfred-style).
            // Guard: don't hide if our own Settings window is what took focus.
            mainWindow.Deactivated += (_, _) => {
                if (!userSettings.StickyWindow
                    && mainWindow.IsVisible
                    && _settingsWindow is not { IsVisible: true }) {
                    mainWindow.Hide();
                    AppHandler.Instance.OnHide();
                }
            };

            // Wire up clipboard so Core code can copy results without depending on Avalonia
            var clipboardService = _services.GetRequiredService<ClipboardService>();
            clipboardService.Initialize(text =>
                Dispatcher.UIThread.InvokeAsync(() => {
                    var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
                    if (clipboard != null) _ = clipboard.SetTextAsync(text);
                }));

            var globalSearch = _services.GetRequiredService<GlobalSearch>();
            desktop.ShutdownRequested += (_, _) => {
                (desktop.MainWindow as MainWindow)?.SavePosition();
                Environment.Exit(0);
            };
            desktop.Exit += async (_, _) => await globalSearch.Stop();

            // Auto-repair: if the saved hotkey is platform-forbidden (e.g. user edited JSON by hand),
            // reset it to the default before registering the global hook.
            if (AppHandler.Instance.IsForbidden(userSettings.ParsedHotkey)) {
                userSettings.Hotkey = HotkeyConfig.Default.ToString();
                userSettings.Save();
            }

            RegisterGlobalHotKey(desktop);

            base.OnFrameworkInitializationCompleted();

            _ = pluginService.StartAsync();

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
            if (desktop.MainWindow is { } mainWindow)
                AppHandler.Instance.ShowWindow(mainWindow);
        });
    }

    public void OpenSettings() {
        if (_settingsWindow is { IsVisible: true }) {
            _settingsWindow.Activate();
            return;
        }
        _settingsVm = _services.GetRequiredService<SettingsWindowViewModel>();
        _settingsWindow = new SettingsWindow {
            DataContext = _settingsVm,
        };
        _settingsWindow.Closed += (_, _) => AppHandler.Instance.HideDockIcon();
        AppHandler.Instance.ShowDockIcon();
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
        services.AddSingleton<AppIconCache>();
        services.AddSingleton<FileIconCache>();
        services.AddSingleton<ApplicationSearch>();
        services.AddSingleton<BrowserDiscovery>();
        services.AddSingleton<TerminalDiscovery>();
        services.AddSingleton<FileSearch>();
        services.AddSingleton<ClipboardService>();
        // TODO: will be replaced in Tarea D with ExchangeRateService + MathJsEngineProvider wiring
        services.AddSingleton<MathJsEngineProvider>();
        services.AddSingleton<MathJsEngine>(sp => {
            var s = sp.GetRequiredService<UserSettings>();
            var fmt = new FormatConfig(
                LargeNumberDecimals: s.CalculatorDecimalPlaces,
                CurrencyA: s.CalculatorCurrencyA,
                CurrencyB: s.CalculatorCurrencyB);
            var baseRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1.0 };
            return new MathJsEngine(baseRates, fmt);
        });
        services.AddSingleton<CalculatorSearch>();
        services.AddSingleton<EmojiDataLoader>();
        services.AddSingleton<EmojiUsageStore>(sp => new EmojiUsageStore(
            AppPaths.EmojiUsageFile,
            sp.GetRequiredService<ILogger<EmojiUsageStore>>()));
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
            sp.GetRequiredService<ClipboardService>(),
            AppPaths.EmojiCacheFile,
            sp.GetRequiredService<EmojiDataLoader>(),
            sp.GetRequiredService<EmojiUsageStore>(),
            sp.GetRequiredService<ILogger<EmojiSearch>>(),
            sp.GetRequiredService<UserSettings>()));

        // Register IInstantSearchSource and IDeferredSearchSource implementations.
        services.AddSingleton<UserDocumentSearch>();
        services.AddSingleton<RandomSearch>();
        services.AddSingleton<PluginService>();
        services.AddSingleton<WebSearchSource>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ApplicationSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<CalculatorSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<EmojiSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<WebSearchSource>());
        services.AddSingleton<DictionarySource>();
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<UserDocumentSearch>());
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<DictionarySource>());
        // services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<RandomSearch>());

        services.AddSingleton<GlobalSearch>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<HistoryService>(sp => new HistoryService(
            sp.GetRequiredService<UserSettings>(),
            sp.GetRequiredService<ILogger<HistoryService>>()));

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static string ComputeLogPath() {
        Directory.CreateDirectory(AppPaths.LogDir);
        return AppPaths.LogFilePattern;
    }

    private static readonly Dictionary<string, KeyCode> KeyNameMap = BuildKeyNameMap();

    private static Dictionary<string, KeyCode> BuildKeyNameMap() {
        var map = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase) {
            ["Space"] = KeyCode.VcSpace,
            ["Enter"] = KeyCode.VcEnter,
            ["Tab"] = KeyCode.VcTab,
            ["Backspace"] = KeyCode.VcBackspace,
            ["Delete"] = KeyCode.VcDelete,
            ["Escape"] = KeyCode.VcEscape,
            [","] = KeyCode.VcComma,
            ["."] = KeyCode.VcPeriod,
            ["-"] = KeyCode.VcMinus,
            ["="] = KeyCode.VcEquals,
            [";"] = KeyCode.VcSemicolon,
            ["/"] = KeyCode.VcSlash,
            ["["] = KeyCode.VcOpenBracket,
            ["]"] = KeyCode.VcCloseBracket,
            ["\\"] = KeyCode.VcBackslash,
            ["'"] = KeyCode.VcQuote,
            ["`"] = KeyCode.VcBackQuote,
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
            
            var mask = e.RawEvent.Mask;
            var hasAlt = mask.HasFlag(EventMask.LeftAlt) || mask.HasFlag(EventMask.RightAlt);
            var hasCtrl = mask.HasFlag(EventMask.LeftCtrl) || mask.HasFlag(EventMask.RightCtrl);
            var hasShift = mask.HasFlag(EventMask.LeftShift) || mask.HasFlag(EventMask.RightShift);
            var hasMeta = mask.HasFlag(EventMask.LeftMeta) || mask.HasFlag(EventMask.RightMeta);
            var hotkey = settings.ParsedHotkey;

            if (e.Data.KeyCode == KeyNameToKeyCode(hotkey.KeyName)
                && hasAlt == hotkey.Alt && hasCtrl == hotkey.Ctrl
                && hasShift == hotkey.Shift && hasMeta == hotkey.Meta) {
                // If Settings is capturing a new hotkey, let the event reach SettingsWindow
                // so it can record the key combination (including the current hotkey itself).
                if (_settingsVm?.IsCapturingHotkey == true)
                    return;

                // Suppress every keydown (including repeats) so the OS never sees it.
                e.SuppressEvent = true;

                // Block key repeat: require the key to be released before accepting
                // the next toggle. _hotkeyDown is cleared in KeyReleased.
                if (_hotkeyDown) return;
                _hotkeyDown = true;

                if (_isToggling) return;
                _isToggling = true;

                // All window state checks happen on the UI thread to avoid accessing
                // Avalonia properties (IsVisible) from the hook thread.
                Dispatcher.UIThread.InvokeAsync(() => {
                    try {
                        var window = desktop.MainWindow;
                        if (window is null) return;

                        var settingsOpen = _settingsWindow is { IsVisible: true };
                        var windowFocused = AppHandler.Instance.IsWindowFocused(window);
                        var settingsFocused = settingsOpen && AppHandler.Instance.IsWindowFocused(_settingsWindow!);

                        if (window.IsVisible) {
                            if (!windowFocused && !settingsFocused) {
                                // App lost focus → bring back to front, keep settings open
                                AppHandler.Instance.FocusWindow(window);
                                if (settingsOpen)
                                    _settingsWindow!.Activate();
                            } else {
                                // App or settings is focused → hide main window; settings stays open
                                window.Hide();
                                AppHandler.Instance.OnHide();
                            }
                        } else {
                            AppHandler.Instance.ShowWindow(window);
                        }
                    } finally {
                        _isToggling = false;
                    }
                });
            }
        };

        _globalHook.KeyReleased += (_, e) => {
            var hotkey = settings.ParsedHotkey;
            if (e.Data.KeyCode == KeyNameToKeyCode(hotkey.KeyName)) {
                e.SuppressEvent = true;
                _hotkeyDown = false;
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
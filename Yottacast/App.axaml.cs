using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
using Yottacast.Core.Search.Clipboard;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Search.Date;
using Yottacast.Core.Search.Dictionary;
using Yottacast.Core.Search.SystemSettings;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Services;
using Yottacast.ViewModels;
using Yottacast.Views;

namespace Yottacast;

public partial class App : Application {
    private IGlobalHook? _globalHook;
    private IClipboardMonitor? _clipboardMonitor;
    private SettingsWindow? _settingsWindow;
    private SettingsWindowViewModel? _settingsVm;
    private MainWindowViewModel? _mainVm;
    private IServiceProvider _services = null!;
    private volatile bool _isToggling = false;
    private volatile bool _hotkeyDown = false;
    private bool _clipboardHotkeyDown;
    private bool _settingsClosing = false;

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            AppHandler.Instance.OnFrameworkInitializationCompleted();
            _services = BuildServices();

            // Wire up live exchange rates
            var exchangeService = _services.GetRequiredService<ExchangeRateService>();
            var engineProvider = _services.GetRequiredService<MathJsEngineProvider>();

            static FormatConfig BuildFormatConfig(UserSettings s) => new(
                LargeNumberDecimals: s.CalculatorDecimalPlaces,
                CurrencyA: s.CalculatorCurrencyA,
                CurrencyB: s.CalculatorCurrencyB);

            var appLogger = _services.GetRequiredService<ILogger<App>>();
            exchangeService.RatesUpdated += rates => {
                var settings = _services.GetRequiredService<UserSettings>();
                _ = Task.Run(async () => {
                    try { await engineProvider.RecreateAsync(rates, BuildFormatConfig(settings)); }
                    catch (Exception ex) { appLogger.LogError(ex, "Failed to recreate MathJsEngine after rates update"); }
                });
            };

            // Recreate engine when user changes calculator settings (format, toggles)
            _services.GetRequiredService<UserSettings>().SearchSettingsChanged += () => {
                var settings = _services.GetRequiredService<UserSettings>();
                var cfg = BuildFormatConfig(settings);
                engineProvider.Current?.UpdateConfig(cfg.LargeNumberDecimals, cfg.CurrencyA, cfg.CurrencyB);
                exchangeService.NotifySettingsChanged();
            };

            _ = exchangeService.StartAsync();

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

            _mainVm = _services.GetRequiredService<MainWindowViewModel>();
            var mainWindowViewModel = _mainVm;
            mainWindowViewModel.Initialize();
            var mainWindow = new MainWindow(userSettings, _services.GetRequiredService<ILogger<MainWindow>>(), _services.GetRequiredService<FileEditorService>()) { DataContext = mainWindowViewModel };
            desktop.MainWindow = mainWindow;
            mainWindow.Topmost = userSettings.StickyWindow;
            userSettings.StickyWindowChanged += () =>
                Dispatcher.UIThread.InvokeAsync(() => mainWindow.Topmost = userSettings.StickyWindow);

            // Auto-hide when losing focus (Alfred-style).
            // Non-sticky: always hide. Sticky: hide only when search box is empty.
            // Guard: don't hide if our own Settings window is what took focus.
            mainWindow.Deactivated += (_, _) => {
                if (_settingsClosing) { _settingsClosing = false; return; }
                if (!mainWindow.IsVisible || _settingsWindow is { IsVisible: true }) return;
                var isEmpty = string.IsNullOrEmpty(mainWindowViewModel.SearchText);
                if (!userSettings.StickyWindow || isEmpty) {
                    mainWindow.Hide();
                    AppHandler.Instance.OnHide();
                }
                // Sticky + non-empty: stay visible. The decay timer starts only when the window
                // actually hides (IsVisible → false), handled in MainWindow.OnPropertyChanged.
            };

            // Wire up clipboard so Core code can copy results without depending on Avalonia
            var clipboardService = _services.GetRequiredService<ClipboardService>();
            clipboardService.Initialize(
                copy: text =>
                    Dispatcher.UIThread.InvokeAsync(() => {
                        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
                        if (clipboard != null) _ = clipboard.SetTextAsync(text);
                    }),
                read: () =>
                    Dispatcher.UIThread.InvokeAsync(async () => {
                        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
                        return clipboard != null ? await clipboard.GetTextAsync() : null;
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
            // Load the persisted clipboard history before starting the monitor. Otherwise the first
            // poll (within ClipboardMonitorIntervalMs) could Add() the current clipboard text, and
            // then LoadAsync would replace _entries wholesale, discarding that entry.
            _ = LoadClipboardThenStartMonitorAsync(_services);
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
        _settingsVm.OpenWithQuery = query =>
            Dispatcher.UIThread.InvokeAsync(() => {
                _mainVm!.SearchText = query;
                var mw = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mw is { IsVisible: false })
                    AppHandler.Instance.ShowWindow(mw);
                _settingsWindow?.Close();
            });
        _settingsWindow = new SettingsWindow {
            DataContext = _settingsVm,
            Topmost = _services.GetRequiredService<UserSettings>().StickyWindow,
        };
        _settingsWindow.Closed += (_, _) => {
            var mw = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mw?.IsVisible == true) _settingsClosing = true;
            AppHandler.Instance.HideDockIcon();
            if (mw?.IsVisible == true) mw.Activate();
        };
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

        // Logger estatico global de Serilog (usado por componentes sin DI, p.ej. LinuxAppHandler).
        // Comparte la misma instancia que AddSerilog; asignacion idempotente.
        Serilog.Log.Logger = serilogLogger;

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
        services.AddSingleton<FaviconCache>();
        services.AddSingleton<FileIconCache>();
        services.AddSingleton<ApplicationSearch>();
        services.AddSingleton<NewlyInstalledAppsSource>();
        services.AddSingleton<ClipboardSearch>();
        services.AddSingleton<BrowserDiscovery>();
        services.AddSingleton<TerminalDiscovery>();
        services.AddSingleton<FileSearch>();
        services.AddSingleton<FileEditorService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ExchangeRateService>();
        services.AddSingleton<MathJsEngineProvider>();
        services.AddSingleton<NerdamerEngine>();
        services.AddSingleton<CalculatorSearch>();
        services.AddSingleton<EmojiLayoutConfig>();
        services.AddSingleton<EmojiDataLoader>();
        services.AddSingleton<EmojiUsageStore>(sp => new EmojiUsageStore(
            AppPaths.EmojiUsageFile,
            sp.GetRequiredService<ILogger<EmojiUsageStore>>()));
        services.AddSingleton<EmojiSearch>(sp => new EmojiSearch(
            sp.GetRequiredService<ClipboardService>(),
            AppPaths.EmojiCacheFile,
            sp.GetRequiredService<EmojiDataLoader>(),
            sp.GetRequiredService<EmojiUsageStore>(),
            sp.GetRequiredService<EmojiLayoutConfig>(),
            sp.GetRequiredService<ILogger<EmojiSearch>>(),
            sp.GetRequiredService<UserSettings>()));

        // Register IInstantSearchSource and IDeferredSearchSource implementations.
        services.AddSingleton<UserDocumentSearch>();
        services.AddSingleton<PluginService>();
        services.AddSingleton<WebSearchSource>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ApplicationSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<CalculatorSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<EmojiSearch>());
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<WebSearchSource>());
        services.AddSingleton<LocalPathSearch>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<LocalPathSearch>());
        services.AddSingleton<UrlSearch>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<UrlSearch>());
        services.AddSingleton<DateSearch>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<DateSearch>());
        services.AddSingleton<DictionarySource>();
        if (OperatingSystem.IsMacOS()) {
            services.AddSingleton<SystemSettingsSearch>(sp => new SystemSettingsSearch(
                sp.GetRequiredService<UserSettings>(),
                sp.GetRequiredService<PlatformProvider>(),
                sp.GetRequiredService<AppIconCache>(),
                sp.GetRequiredService<ILogger<SystemSettingsSearch>>()));
            services.AddSingleton<IInstantSearchSource>(
                sp => sp.GetRequiredService<SystemSettingsSearch>());
        }
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<UserDocumentSearch>());
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<DictionarySource>());
        services.AddSingleton<RandomSearch>();
        services.AddSingleton<IDeferredSearchSource>(sp => sp.GetRequiredService<RandomSearch>());

        // Register IEmptyStateSource implementations
        services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<NewlyInstalledAppsSource>());
        services.AddSingleton<IEmptyStateSource>(sp => sp.GetRequiredService<ClipboardSearch>());

        services.AddSingleton<GlobalSearch>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<HistoryService>(sp => new HistoryService(
            sp.GetRequiredService<UserSettings>(),
            sp.GetRequiredService<ILogger<HistoryService>>()));
        services.AddSingleton<LaunchHistory>(sp => new LaunchHistory(
            AppPaths.LaunchHistoryFile,
            sp.GetRequiredService<ILogger<LaunchHistory>>()));
        services.AddSingleton<ClipboardHistoryStore>(sp => new ClipboardHistoryStore(
            AppPaths.ClipboardHistoryFile,
            sp.GetRequiredService<ILogger<ClipboardHistoryStore>>()));
        services.AddSingleton<ClipboardHistorySearch>();
        services.AddSingleton<IInstantSearchSource>(sp => sp.GetRequiredService<ClipboardHistorySearch>());

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

    private async Task LoadClipboardThenStartMonitorAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<ClipboardHistoryStore>();
        await store.LoadAsync().ConfigureAwait(false);
        SetupClipboardMonitor(services);
    }

    private void SetupClipboardMonitor(IServiceProvider services)
    {
        var settings = services.GetRequiredService<UserSettings>();
        var store    = services.GetRequiredService<ClipboardHistoryStore>();

        // Wire the user-configured limits into the store, both at startup and whenever settings change.
        void ApplyLimitsFromSettings()
        {
            store.MaxEntries = settings.ClipboardHistoryMaxEntries;
            store.MaxDays    = settings.ClipboardHistoryMaxDays;
            store.ApplyLimitsNow();
        }

        ApplyLimitsFromSettings();

        void StartMonitor()
        {
            if (settings.ClipboardSearchVisibility == SearchSourceVisibility.Disabled) return;
            var prev = Interlocked.Exchange(ref _clipboardMonitor, null);
            prev?.Stop();
            IClipboardMonitor monitor;
            if (OperatingSystem.IsMacOS())
                monitor = new MacClipboardMonitor(
                    services.GetRequiredService<ILogger<MacClipboardMonitor>>());
            else if (OperatingSystem.IsWindows())
                monitor = new WindowsClipboardMonitor(
                    services.GetRequiredService<ILogger<WindowsClipboardMonitor>>());
            else return;

            monitor.TextCopied += text => store.Add(text);
            monitor.Start();
            _clipboardMonitor = monitor;
        }

        void StopMonitor()
        {
            var prev = Interlocked.Exchange(ref _clipboardMonitor, null);
            prev?.Stop();
        }

        StartMonitor();

        settings.SearchSettingsChanged += () =>
        {
            ApplyLimitsFromSettings();
            if (settings.ClipboardSearchVisibility != SearchSourceVisibility.Disabled)
                StartMonitor();
            else
                StopMonitor();
        };
    }

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
                                // App lost focus → just hide, no OnHide() since we didn't have focus
                                window.Hide();
                            } else {
                                // App or settings is focused → hide main window
                                window.Hide();
                                if (settingsOpen) {
                                    // Settings is open: give it focus instead of restoring the previous app.
                                    // OnHide() is skipped so _previousApp is preserved for the next hide.
                                    _settingsWindow!.Activate();
                                } else {
                                    AppHandler.Instance.OnHide();
                                }
                            }
                        } else {
                            AppHandler.Instance.ShowWindow(window);
                            if (window.DataContext is MainWindowViewModel vm)
                                vm.OnWindowShow();
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

        // Hotkey global para abrir directamente en modo Clipboard — lee de settings en cada evento
        // para que los cambios en Settings tengan efecto inmediato sin reiniciar.
        _globalHook.KeyPressed += (_, e) => {
            var ch = settings.ParsedClipboardHotkey;
            if (ch == null) return;
            if (settings.ClipboardSearchVisibility != SearchSourceVisibility.ModeOnly) return;

            var mask = e.RawEvent.Mask;
            var hasAlt  = mask.HasFlag(EventMask.LeftAlt)  || mask.HasFlag(EventMask.RightAlt);
            var hasCtrl = mask.HasFlag(EventMask.LeftCtrl) || mask.HasFlag(EventMask.RightCtrl);
            var hasShift= mask.HasFlag(EventMask.LeftShift)|| mask.HasFlag(EventMask.RightShift);
            var hasMeta = mask.HasFlag(EventMask.LeftMeta) || mask.HasFlag(EventMask.RightMeta);

            if (e.Data.KeyCode != KeyNameToKeyCode(ch.KeyName)) return;
            if (hasAlt != ch.Alt || hasCtrl != ch.Ctrl || hasShift != ch.Shift || hasMeta != ch.Meta) return;

            // Dejar pasar el evento a Settings si está capturando el clipboard hotkey
            if (_settingsVm?.IsCapturingClipboardHotkey == true) return;

            e.SuppressEvent = true;
            if (_clipboardHotkeyDown) return;
            _clipboardHotkeyDown = true;

            Dispatcher.UIThread.InvokeAsync(() => {
                var window = desktop.MainWindow;
                if (window is null) return;
                if (window.DataContext is not MainWindowViewModel vm) return;
                if (!window.IsVisible) {
                    AppHandler.Instance.ShowWindow(window);
                    vm.ActivateMode(SearchMode.Clipboard);
                } else if (vm.ClipboardModeActive) {
                    window.Hide();
                } else {
                    vm.ActivateMode(SearchMode.Clipboard);
                }
            });
        };

        _globalHook.KeyReleased += (_, e) => {
            var ch = settings.ParsedClipboardHotkey;
            if (ch != null && e.Data.KeyCode == KeyNameToKeyCode(ch.KeyName)) {
                e.SuppressEvent = true;
                _clipboardHotkeyDown = false;
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
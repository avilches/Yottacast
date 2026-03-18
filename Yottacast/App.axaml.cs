using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SharpHook.Data;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.Storage;
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
            _services = BuildServices();

            var searchService = _services.GetRequiredService<GlobalSearch>();
            _ = searchService.Start();

            var userSettings = _services.GetRequiredService<UserSettings>();
            ThemeService.Apply(userSettings.Theme);

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            desktop.MainWindow = new MainWindow {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.Exit += (_, _) => searchService.Stop();

            RegisterGlobalHotKey(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OpenSettings() {
        if (_settingsWindow is { IsVisible: true }) {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow {
            DataContext = _services.GetRequiredService<SettingsWindowViewModel>(),
        };
        _settingsWindow.Show();
    }

    private static IServiceProvider BuildServices() {
        var services = new ServiceCollection();

        services.AddSingleton(_ => UserSettings.Load());
        services.AddSingleton<ApplicationSearch>();
        services.AddSingleton<BrowserDiscovery>();
        services.AddSingleton<TerminalDiscovery>();

        // Register ApplicationStorage as the active ISearchSource.
        // Add BrowserDiscovery / TerminalDiscovery here when ready.
        services.AddSingleton<FileStorage>();
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<ApplicationSearch>());
        services.AddSingleton<ISearchSource>(sp => sp.GetRequiredService<FileStorage>());

        services.AddSingleton<GlobalSearch>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private void RegisterGlobalHotKey(IClassicDesktopStyleApplicationLifetime desktop) {
        _globalHook = new TaskPoolGlobalHook();
        _globalHook.KeyPressed += (_, e) => {
            var isAlt = e.RawEvent.Mask.HasFlag(EventMask.LeftAlt) ||
                        e.RawEvent.Mask.HasFlag(EventMask.RightAlt);
            if (e.Data.KeyCode == KeyCode.VcSpace && isAlt) {
                Console.WriteLine($"[Hook] ALT+Space detected");
                Dispatcher.UIThread.InvokeAsync(() => {
                    var window = desktop.MainWindow;
                    if (window is null) return;
                    Console.WriteLine($"[Hook] UI thread - window.IsVisible={window.IsVisible}");
                    if (window.IsVisible) {
                        window.Hide();
                    } else {
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

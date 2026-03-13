using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using System.Linq;
using Avalonia.Markup.Xaml;
using SharpHook;
using SharpHook.Data;
using Yottacast.ViewModels;
using Yottacast.Views;

namespace Yottacast;

public partial class App : Application {
    private IGlobalHook? _globalHook;

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow {
                DataContext = new MainWindowViewModel(),
            };
            RegisterGlobalHotKey(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalHotKey(IClassicDesktopStyleApplicationLifetime desktop) {
        _globalHook = new TaskPoolGlobalHook();
        _globalHook.KeyPressed += (_, e) => {
            var isAlt = e.RawEvent.Mask.HasFlag(EventMask.LeftAlt) ||
                        e.RawEvent.Mask.HasFlag(EventMask.RightAlt);
            if (e.Data.KeyCode == KeyCode.VcSpace && isAlt) {
                Console.WriteLine($"[Hook] ALT+Space detected"); //, IsEnabled={e.}");
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
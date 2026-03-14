using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public record ThemeOption(string Id, string DisplayName);

public partial class SettingsWindowViewModel : ViewModelBase {
    [ObservableProperty] private string? _selectedBrowser;
    [ObservableProperty] private string? _selectedTerminal;
    [ObservableProperty] private ThemeOption? _selectedTheme;

    public IReadOnlyList<string> Browsers  { get; }
    public IReadOnlyList<string> Terminals { get; }
    public IReadOnlyList<ThemeOption> Themes { get; }

    private readonly UserSettings _settings;

    public SettingsWindowViewModel(UserSettings settings) {
        _settings = settings;

        Browsers  = BrowserDiscovery.Discover().Select(b => b.Name).ToList();
        Terminals = TerminalDiscovery.Discover().Select(t => t.Name).ToList();
        Themes    = LoadThemes();

        // Set initial selections without triggering the partial callbacks (fields, not properties)
        _selectedBrowser  = Browsers.Contains(settings.Browser) ? settings.Browser : Browsers.FirstOrDefault();
        _selectedTerminal = Terminals.Contains(settings.Terminal) ? settings.Terminal : Terminals.FirstOrDefault();
        _selectedTheme    = Themes.FirstOrDefault(t => t.Id == settings.Theme) ?? Themes.FirstOrDefault();
    }

    partial void OnSelectedBrowserChanged(string? value) {
        _settings.Browser = value ?? "";
        _settings.Save();
    }

    partial void OnSelectedTerminalChanged(string? value) {
        _settings.Terminal = value ?? "";
        _settings.Save();
    }

    partial void OnSelectedThemeChanged(ThemeOption? value) {
        if (value is null) return;
        _settings.Theme = value.Id;
        _settings.Save();
        ThemeService.Apply(value.Id);
    }

    private static IReadOnlyList<ThemeOption> LoadThemes() {
        var folder = Path.Combine(AppContext.BaseDirectory, "Themes");
        var themes = new List<ThemeOption>();

        try {
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(f => f)) {
                var id = Path.GetFileNameWithoutExtension(file);
                if (id == "settings") continue;
                try {
                    var json = JsonNode.Parse(File.ReadAllText(file));
                    var displayName = json?["name"]?.GetValue<string>() ?? id;
                    themes.Add(new ThemeOption(id, displayName));
                } catch {
                    themes.Add(new ThemeOption(id, id));
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"[Settings] Could not load themes: {ex.Message}");
        }

        // Always include the built-in default as fallback
        if (themes.Count == 0)
            themes.Add(new ThemeOption("dark-default", "Dark Default"));

        return themes;
    }
}

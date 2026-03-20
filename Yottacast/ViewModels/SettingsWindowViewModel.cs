using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public partial class SettingsWindowViewModel : ViewModelBase {
    [ObservableProperty] private string? _selectedBrowser;
    [ObservableProperty] private string? _selectedTerminal;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private bool _isCapturingHotkey;

    public IReadOnlyList<string> Browsers  { get; }
    public IReadOnlyList<string> Terminals { get; }
    public IReadOnlyList<ThemeOption> Themes { get; }

    private readonly UserSettings _settings;
    private readonly ThemeService _themeService;

    public SettingsWindowViewModel(
        UserSettings settings,
        BrowserDiscovery browserDiscovery,
        TerminalDiscovery terminalDiscovery,
        ThemeService themeService) {
        _settings = settings;
        _themeService = themeService;

        Browsers  = browserDiscovery.Discover().Select(b => b.Name).ToList();
        Terminals = terminalDiscovery.Discover().Select(t => t.Name).ToList();
        Themes    = themeService.AvailableThemes();

        // Self-heal stored browser/terminal before reading them for the picker.
        settings.EnsureIntegrity();

        // Set initial selections without triggering the partial callbacks (fields, not properties)
        _selectedBrowser  = Browsers.Contains(settings.Browser) ? settings.Browser : Browsers.FirstOrDefault();
        _selectedTerminal = Terminals.Contains(settings.Terminal) ? settings.Terminal : Terminals.FirstOrDefault();
        _selectedTheme    = Themes.FirstOrDefault(t => t.Id == settings.Theme) ?? Themes.FirstOrDefault();
        _hotkeyText       = settings.Hotkey;
    }

    public string HotkeyDisplayText => IsCapturingHotkey ? "Press keys\u2026" : HotkeyText;

    partial void OnIsCapturingHotkeyChanged(bool value) => OnPropertyChanged(nameof(HotkeyDisplayText));
    partial void OnHotkeyTextChanged(string value)      => OnPropertyChanged(nameof(HotkeyDisplayText));

    public void StartHotkeyCapture() => IsCapturingHotkey = true;

    public void CancelHotkeyCapture() {
        IsCapturingHotkey = false;
        HotkeyText = _settings.Hotkey;
    }

    public void ProcessKeyCapture(Key key, KeyModifiers mods) {
        if (IsModifierOnly(key)) return;
        if (key == Key.Escape) { CancelHotkeyCapture(); return; }

        var config = new HotkeyConfig(
            Alt:     mods.HasFlag(KeyModifiers.Alt),
            Ctrl:    mods.HasFlag(KeyModifiers.Control),
            Shift:   mods.HasFlag(KeyModifiers.Shift),
            Meta:    mods.HasFlag(KeyModifiers.Meta),
            KeyName: AvaloniaKeyToName(key));

        _settings.Hotkey = config.ToString();
        _settings.Save();
        HotkeyText = _settings.Hotkey;
        IsCapturingHotkey = false;
    }

    private static bool IsModifierOnly(Key k) =>
        k is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
          or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string AvaloniaKeyToName(Key k) => k switch {
        Key.Space => "Space",
        _ => k.ToString(),
    };

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
        _themeService.Apply(value.Id);
    }
}

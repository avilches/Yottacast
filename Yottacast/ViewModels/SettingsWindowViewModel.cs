using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji
}

public partial class SettingsWindowViewModel : ViewModelBase {
    // ── Section navigation ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppSearchSelected))]
    [NotifyPropertyChangedFor(nameof(IsWebSearchSelected))]
    [NotifyPropertyChangedFor(nameof(IsFileSearchSelected))]
    [NotifyPropertyChangedFor(nameof(IsCalculatorSelected))]
    [NotifyPropertyChangedFor(nameof(IsClipboardSelected))]
    [NotifyPropertyChangedFor(nameof(IsEmojiSelected))]
    private SettingsSection _selectedSection = SettingsSection.General;

    public bool IsGeneralSelected   => SelectedSection == SettingsSection.General;
    public bool IsAppSearchSelected => SelectedSection == SettingsSection.AppSearch;
    public bool IsWebSearchSelected => SelectedSection == SettingsSection.WebSearch;
    public bool IsFileSearchSelected => SelectedSection == SettingsSection.FileSearch;
    public bool IsCalculatorSelected => SelectedSection == SettingsSection.Calculator;
    public bool IsClipboardSelected  => SelectedSection == SettingsSection.Clipboard;
    public bool IsEmojiSelected      => SelectedSection == SettingsSection.Emoji;

    [RelayCommand] private void SelectGeneral()   => SelectedSection = SettingsSection.General;
    [RelayCommand] private void SelectAppSearch() => SelectedSection = SettingsSection.AppSearch;
    [RelayCommand] private void SelectWebSearch() => SelectedSection = SettingsSection.WebSearch;
    [RelayCommand] private void SelectFileSearch() => SelectedSection = SettingsSection.FileSearch;
    [RelayCommand] private void SelectCalculator() => SelectedSection = SettingsSection.Calculator;
    [RelayCommand] private void SelectClipboard()  => SelectedSection = SettingsSection.Clipboard;
    [RelayCommand] private void SelectEmoji()      => SelectedSection = SettingsSection.Emoji;

    // ── General section ──────────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedBrowser;
    [ObservableProperty] private string? _selectedTerminal;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private bool _isCapturingHotkey;

    public IReadOnlyList<string> Browsers  { get; }
    public IReadOnlyList<string> Terminals { get; }
    public IReadOnlyList<ThemeOption> Themes { get; }

    // ── Folder lists ─────────────────────────────────────────────────────────
    public ObservableCollection<string> SearchFolders  { get; }
    public ObservableCollection<string> AppDirectories { get; }

    // ── Web Search engines ───────────────────────────────────────────────────
    public IReadOnlyList<WebSearchEngineRowViewModel> WebSearchEngines { get; }

    // ── Feature toggles ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableCalculator;
    [ObservableProperty] private bool _enableClipboard;
    [ObservableProperty] private bool _enableEmoji;

    partial void OnEnableCalculatorChanged(bool v) { _settings.EnableCalculator = v; _settings.Save(); }
    partial void OnEnableClipboardChanged(bool v)  { _settings.EnableClipboard  = v; _settings.Save(); }
    partial void OnEnableEmojiChanged(bool v)      { _settings.EnableEmoji      = v; _settings.Save(); }

    // ── Infrastructure ───────────────────────────────────────────────────────
    private readonly UserSettings _settings;
    private readonly ThemeService _themeService;

    public SettingsWindowViewModel(
        UserSettings settings,
        BrowserDiscovery browserDiscovery,
        TerminalDiscovery terminalDiscovery,
        ThemeService themeService) {
        _settings    = settings;
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

        _enableCalculator = settings.EnableCalculator;
        _enableClipboard  = settings.EnableClipboard;
        _enableEmoji      = settings.EnableEmoji;

        SearchFolders  = new ObservableCollection<string>(settings.SearchFolders);
        AppDirectories = new ObservableCollection<string>(settings.AppDirectories);

        SearchFolders.CollectionChanged  += (_, _) => { settings.SearchFolders  = SearchFolders.ToList();  settings.Save(); };
        AppDirectories.CollectionChanged += (_, _) => { settings.AppDirectories = AppDirectories.ToList(); settings.Save(); };

        WebSearchEngines = WebSearchDefaults.Engines.Select(engine => {
            var cfg = settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                      ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);
            return new WebSearchEngineRowViewModel(engine.Id, engine.Name, engine.QueryUrl, cfg, settings);
        }).ToList();
    }

    // ── Folder mutators (called from code-behind) ─────────────────────────────
    public void AddSearchFolder(string path) {
        if (!SearchFolders.Contains(path)) SearchFolders.Add(path);
    }

    public void RemoveSearchFolder(string path) => SearchFolders.Remove(path);

    public void AddAppDirectory(string path) {
        if (!AppDirectories.Contains(path)) AppDirectories.Add(path);
    }

    public void RemoveAppDirectory(string path) => AppDirectories.Remove(path);

    // ── Hotkey capture ────────────────────────────────────────────────────────
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

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

    // Modifier symbols — OS-specific, fixed at runtime
    public string CtrlSymbol  => AppHandler.Instance.CtrlSymbol;
    public string AltSymbol   => AppHandler.Instance.AltSymbol;
    public string ShiftSymbol => AppHandler.Instance.ShiftSymbol;
    public string MetaSymbol  => AppHandler.Instance.MetaSymbol;

    // Real-time modifier state during capture (updated on every KeyDown/KeyUp)
    private KeyModifiers _capturingModifiers = KeyModifiers.None;

    // Badge active state: during capture reflects physically-held mods; otherwise reflects saved hotkey
    public bool BadgeCtrlActive  => IsCapturingHotkey ? _capturingModifiers.HasFlag(KeyModifiers.Control) : _settings.ParsedHotkey.Ctrl;
    public bool BadgeAltActive   => IsCapturingHotkey ? _capturingModifiers.HasFlag(KeyModifiers.Alt)     : _settings.ParsedHotkey.Alt;
    public bool BadgeShiftActive => IsCapturingHotkey ? _capturingModifiers.HasFlag(KeyModifiers.Shift)   : _settings.ParsedHotkey.Shift;
    public bool BadgeMetaActive  => IsCapturingHotkey ? _capturingModifiers.HasFlag(KeyModifiers.Meta)    : _settings.ParsedHotkey.Meta;

    // Central key text: key name when idle; "Press a key" if a modifier is held; else "Press a modifier"
    public string HotkeyKeyText {
        get {
            if (!IsCapturingHotkey) return _settings.ParsedHotkey.KeyName;
            return _capturingModifiers != KeyModifiers.None ? "Press a key\u2026" : "Press a modifier\u2026";
        }
    }

    public void StartHotkeyCapture() {
        _capturingModifiers = KeyModifiers.None;
        IsCapturingHotkey   = true;
        NotifyBadgesAndKey();
    }

    public void CancelHotkeyCapture() {
        _capturingModifiers = KeyModifiers.None;
        IsCapturingHotkey   = false;
        NotifyBadgesAndKey();
    }

    // Called from code-behind on every KeyDown/KeyUp during capture to update modifier badges
    public void UpdateCapturingModifiers(KeyModifiers mods) {
        _capturingModifiers = mods;
        NotifyBadgesAndKey();
    }

    public void ProcessKeyCapture(Key key, KeyModifiers mods) {
        if (key == Key.Escape) { CancelHotkeyCapture(); return; }
        if (mods == KeyModifiers.None) return;  // require at least one modifier held

        var config = new HotkeyConfig(
            Alt:     mods.HasFlag(KeyModifiers.Alt),
            Ctrl:    mods.HasFlag(KeyModifiers.Control),
            Shift:   mods.HasFlag(KeyModifiers.Shift),
            Meta:    mods.HasFlag(KeyModifiers.Meta),
            KeyName: AvaloniaKeyToName(key));

        if (AppHandler.Instance.IsForbidden(config)) return;  // ignore silently

        _settings.Hotkey = config.ToString();
        _settings.Save();
        _capturingModifiers = KeyModifiers.None;
        IsCapturingHotkey   = false;
        NotifyBadgesAndKey();
    }

    private void NotifyBadgesAndKey() {
        OnPropertyChanged(nameof(BadgeCtrlActive));
        OnPropertyChanged(nameof(BadgeAltActive));
        OnPropertyChanged(nameof(BadgeShiftActive));
        OnPropertyChanged(nameof(BadgeMetaActive));
        OnPropertyChanged(nameof(HotkeyKeyText));
    }

    private static string AvaloniaKeyToName(Key k) => k switch {
        Key.Space          => "Space",
        Key.Enter          => "Enter",
        Key.Tab            => "Tab",
        Key.Back           => "Backspace",
        Key.Delete         => "Delete",
        Key.Escape         => "Escape",
        Key.OemComma       => ",",
        Key.OemPeriod      => ".",
        Key.OemMinus       => "-",
        Key.OemPlus        => "=",
        Key.OemSemicolon   => ";",
        Key.OemQuestion    => "/",
        Key.OemOpenBrackets  => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe        => "\\",
        Key.OemBackslash   => "\\",
        Key.OemQuotes      => "'",
        Key.OemTilde       => "`",
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.F1 and <= Key.F12 => $"F{k - Key.F1 + 1}",
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

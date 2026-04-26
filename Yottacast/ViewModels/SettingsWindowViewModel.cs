using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Services;

namespace Yottacast.ViewModels;

public enum SettingsSection {
    General, AppSearch, WebSearch, FileSearch, Calculator, Clipboard, Emoji, Dictionary, History
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
    [NotifyPropertyChangedFor(nameof(IsDictionarySelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySelected))]
    private SettingsSection _selectedSection = SettingsSection.General;

    partial void OnSelectedSectionChanged(SettingsSection oldValue, SettingsSection newValue) {
        if (oldValue == SettingsSection.AppSearch)
            FlushAppDirectoryChanges();
        _logger.LogInformation("Settings: section {Old} → {New}", oldValue, newValue);
    }

    public bool IsGeneralSelected   => SelectedSection == SettingsSection.General;
    public bool IsAppSearchSelected => SelectedSection == SettingsSection.AppSearch;
    public bool IsWebSearchSelected => SelectedSection == SettingsSection.WebSearch;
    public bool IsFileSearchSelected => SelectedSection == SettingsSection.FileSearch;
    public bool IsCalculatorSelected => SelectedSection == SettingsSection.Calculator;
    public bool IsClipboardSelected  => SelectedSection == SettingsSection.Clipboard;
    public bool IsEmojiSelected      => SelectedSection == SettingsSection.Emoji;
    public bool IsDictionarySelected => SelectedSection == SettingsSection.Dictionary;
    public bool IsHistorySelected    => SelectedSection == SettingsSection.History;

    [RelayCommand] private void SelectGeneral()   => SelectedSection = SettingsSection.General;
    [RelayCommand] private void SelectAppSearch() => SelectedSection = SettingsSection.AppSearch;
    [RelayCommand] private void SelectWebSearch() => SelectedSection = SettingsSection.WebSearch;
    [RelayCommand] private void SelectFileSearch() => SelectedSection = SettingsSection.FileSearch;
    [RelayCommand] private void SelectCalculator() => SelectedSection = SettingsSection.Calculator;
    [RelayCommand] private void SelectClipboard()  => SelectedSection = SettingsSection.Clipboard;
    [RelayCommand] private void SelectEmoji()      => SelectedSection = SettingsSection.Emoji;
    [RelayCommand] private void SelectDictionary() => SelectedSection = SettingsSection.Dictionary;
    [RelayCommand] private void SelectHistory()    => SelectedSection = SettingsSection.History;

    // ── General section ──────────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedBrowser;
    [ObservableProperty] private string? _selectedTerminal;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private bool _isCapturingHotkey;

    [ObservableProperty] private IReadOnlyList<string> _browsers = [];
    [ObservableProperty] private IReadOnlyList<string> _terminals = [];
    [ObservableProperty] private bool _isBrowsersLoading;
    [ObservableProperty] private bool _isTerminalsLoading;
    [ObservableProperty] private IReadOnlyList<ThemeOption> _themes = [];

    // ── Folder lists ─────────────────────────────────────────────────────────
    public ObservableCollection<SearchFolderItem> SearchFolders { get; }
    public ObservableCollection<string> AppDirectories { get; }

    // ── Web Search engines ───────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<WebSearchGroupViewModel> _webSearchGroups = [];

    [ObservableProperty] private bool _showDisabledWebSearchEngines;

    // ── Feature toggles ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableAppSearch;
    [ObservableProperty] private bool _enableCalculator;
    [ObservableProperty] private bool _enableClipboard;
    [ObservableProperty] private bool _enableEmoji;
    [ObservableProperty] private bool _enableFileSearch;
    [ObservableProperty] private bool _enableWebSearch;
    [ObservableProperty] private bool _fileSearchOnlySpecificFolders;
    [ObservableProperty] private bool _stickyWindow;
    [ObservableProperty] private bool _enableHistory;
    [ObservableProperty] private int _historyMaxItems;
    [ObservableProperty] private string _historyDisplayText = "(empty)";

    partial void OnEnableAppSearchChanged(bool value)               { _settings.EnableAppSearch              = value; _settings.Save(); _logger.LogInformation("Settings: EnableAppSearch = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnEnableCalculatorChanged(bool value)              { _settings.EnableCalculator             = value; _settings.Save(); _logger.LogInformation("Settings: EnableCalculator = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnEnableClipboardChanged(bool value)               { _settings.EnableClipboard              = value; _settings.Save(); _logger.LogInformation("Settings: EnableClipboard = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnEnableEmojiChanged(bool value)                   { _settings.EnableEmoji                  = value; _settings.Save(); _logger.LogInformation("Settings: EnableEmoji = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnEnableFileSearchChanged(bool value)              { _settings.EnableFileSearch             = value; _settings.Save(); _logger.LogInformation("Settings: EnableFileSearch = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnEnableWebSearchChanged(bool value)               { _settings.EnableWebSearch              = value; _settings.Save(); _logger.LogInformation("Settings: EnableWebSearch = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnShowDisabledWebSearchEnginesChanged(bool value) {
        _settings.ShowDisabledWebSearchEngines = value;
        _settings.Save();
        _logger.LogInformation("Settings: ShowDisabledWebSearchEngines = {Value}", value);
        foreach (var group in WebSearchGroups)
            foreach (var engine in group.Engines)
                engine.ShowDisabled = value;
    }
    partial void OnFileSearchOnlySpecificFoldersChanged(bool value) { _settings.FileSearchOnlySpecificFolders = value; _settings.Save(); _logger.LogInformation("Settings: FileSearchOnlySpecificFolders = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnStickyWindowChanged(bool value)                  { _settings.StickyWindow                 = value; _settings.Save(); _logger.LogInformation("Settings: StickyWindow = {Value}", value); }

    partial void OnEnableHistoryChanged(bool value) {
        _settings.EnableHistory = value;
        _settings.Save();
        _logger.LogInformation("Settings: EnableHistory = {Value}", value);
    }

    partial void OnHistoryMaxItemsChanged(int value) {
        if (value is < 1 or > AppDefaults.HistoryMaxItems) return;
        _settings.HistoryMaxItems = value;
        _settings.Save();
        _logger.LogInformation("Settings: HistoryMaxItems = {Value}", value);
    }

    // ── Dictionary config ────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableDictionary;
    [ObservableProperty] private string _dictionaryPrefix = AppDefaults.DictionaryDefaultPrefix;
    [ObservableProperty] private bool _dictionaryShowAlways;
    public ObservableCollection<DictionaryLanguageItem> DictionaryLanguages { get; private set; } = [];

    partial void OnEnableDictionaryChanged(bool value)    { _settings.EnableDictionary    = value; _settings.Save(); _logger.LogInformation("Settings: EnableDictionary = {Value}", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnDictionaryPrefixChanged(string value)  { _settings.DictionaryPrefix    = value; _settings.Save(); _logger.LogInformation("Settings: DictionaryPrefix = \"{Value}\"", value); _settings.NotifySearchSettingsChanged(); }
    partial void OnDictionaryShowAlwaysChanged(bool value) { _settings.DictionaryShowAlways = value; _settings.Save(); _logger.LogInformation("Settings: DictionaryShowAlways = {Value}", value); _settings.NotifySearchSettingsChanged(); }

    // ── Calculator config ────────────────────────────────────────────────────
    [ObservableProperty] private string _calculatorCurrencyA = "EUR";
    [ObservableProperty] private string _calculatorCurrencyB = "USD";
    [ObservableProperty] private int _calculatorDecimalPlaces = 2;
    [ObservableProperty] private bool _calculatorIncludeMetals;
    [ObservableProperty] private bool _calculatorIncludeCrypto;
    [ObservableProperty] private decimal _exchangeRateRefreshIntervalHours;

    partial void OnCalculatorCurrencyAChanged(string value) {
        var upper = value.ToUpperInvariant();
        _settings.CalculatorCurrencyA = upper;
        _settings.Save();
        _logger.LogInformation("Settings: CalculatorCurrencyA = \"{Value}\"", upper);
        _settings.NotifySearchSettingsChanged();
    }
    partial void OnCalculatorCurrencyBChanged(string value) {
        var upper = value.ToUpperInvariant();
        _settings.CalculatorCurrencyB = upper;
        _settings.Save();
        _logger.LogInformation("Settings: CalculatorCurrencyB = \"{Value}\"", upper);
        _settings.NotifySearchSettingsChanged();
    }
    partial void OnCalculatorDecimalPlacesChanged(int value) {
        _settings.CalculatorDecimalPlaces = value;
        _settings.Save();
        _logger.LogInformation("Settings: CalculatorDecimalPlaces = {Value}", value);
        _settings.NotifySearchSettingsChanged();
    }
    partial void OnCalculatorIncludeMetalsChanged(bool value) {
        _settings.CalculatorIncludeMetals = value;
        _settings.Save();
        _logger.LogInformation("Settings: CalculatorIncludeMetals = {Value}", value);
        _exchangeRateService.NotifySettingsChanged();
        _settings.NotifySearchSettingsChanged();
    }
    partial void OnCalculatorIncludeCryptoChanged(bool value) {
        _settings.CalculatorIncludeCrypto = value;
        _settings.Save();
        _logger.LogInformation("Settings: CalculatorIncludeCrypto = {Value}", value);
        _exchangeRateService.NotifySettingsChanged();
        _settings.NotifySearchSettingsChanged();
    }
    partial void OnExchangeRateRefreshIntervalHoursChanged(decimal value) {
        var clamped = (int)Math.Clamp(value, 1, 168);
        _settings.ExchangeRateRefreshIntervalHours = clamped;
        _settings.Save();
        _logger.LogInformation("Settings: ExchangeRateRefreshIntervalHours = {Value}", clamped);
    }

    public string ExchangeRatesLastUpdatedText {
        get {
            var dt = _exchangeRateService.LastUpdated;
            if (dt == null) return "Never";
            var local = dt.Value.ToLocalTime();
            return local.ToString("g");
        }
    }

    // ── App version ──────────────────────────────────────────────────────────
    public string AppVersion { get; } =
        "Yottacast " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—");

    // ── Infrastructure ───────────────────────────────────────────────────────
    private readonly UserSettings _settings;
    private readonly ThemeService _themeService;
    private readonly PlatformProvider _platform;
    private readonly PluginService _pluginService;
    private readonly BrowserDiscovery _browserDiscovery;
    private readonly TerminalDiscovery _terminalDiscovery;
    private readonly ExchangeRateService _exchangeRateService;
    private readonly ILogger<SettingsWindowViewModel> _logger;
    private bool _appDirectoriesDirty;
    private readonly HistoryService _historyService;

    public SettingsWindowViewModel(
        UserSettings settings,
        BrowserDiscovery browserDiscovery,
        TerminalDiscovery terminalDiscovery,
        ThemeService themeService,
        PlatformProvider platform,
        PluginService pluginService,
        HistoryService historyService,
        ExchangeRateService exchangeRateService,
        ILogger<SettingsWindowViewModel> logger) {
        _settings             = settings;
        _themeService         = themeService;
        _platform             = platform;
        _pluginService        = pluginService;
        _browserDiscovery     = browserDiscovery;
        _terminalDiscovery    = terminalDiscovery;
        _exchangeRateService  = exchangeRateService;
        _logger               = logger;
        _logger.LogInformation("Settings: opened");

        _themes = themeService.AvailableThemes();
        themeService.ThemesChanged += OnThemesChanged;

        // Self-heal stored browser/terminal before reading them for the picker.
        settings.EnsureIntegrity();

        // Start with only the saved selection; full list is loaded lazily on dropdown open.
        _browsers  = string.IsNullOrEmpty(settings.Browser)  ? [] : [settings.Browser];
        _terminals = string.IsNullOrEmpty(settings.Terminal) ? [] : [settings.Terminal];

        _selectedBrowser  = Browsers.FirstOrDefault();
        _selectedTerminal = Terminals.FirstOrDefault();
        _selectedTheme    = Themes.FirstOrDefault(t => t.Id == settings.Theme) ?? Themes.FirstOrDefault();

        _enableAppSearch                 = settings.EnableAppSearch;
        _enableCalculator                = settings.EnableCalculator;
        _enableClipboard                 = settings.EnableClipboard;
        _enableEmoji                     = settings.EnableEmoji;
        _enableFileSearch                = settings.EnableFileSearch;
        _enableWebSearch                 = settings.EnableWebSearch;
        _fileSearchOnlySpecificFolders   = settings.FileSearchOnlySpecificFolders;
        _stickyWindow                    = settings.StickyWindow;
        _calculatorCurrencyA                  = settings.CalculatorCurrencyA;
        _calculatorCurrencyB                  = settings.CalculatorCurrencyB;
        _calculatorDecimalPlaces              = settings.CalculatorDecimalPlaces;
        _calculatorIncludeMetals              = settings.CalculatorIncludeMetals;
        _calculatorIncludeCrypto              = settings.CalculatorIncludeCrypto;
        _exchangeRateRefreshIntervalHours     = settings.ExchangeRateRefreshIntervalHours;
        _enableDictionary                     = settings.EnableDictionary;
        _dictionaryPrefix                = settings.DictionaryPrefix;
        _dictionaryShowAlways            = settings.DictionaryShowAlways;

        _historyService = historyService;
        _enableHistory = settings.EnableHistory;
        _historyMaxItems = settings.HistoryMaxItems;
        _historyDisplayText = BuildHistoryDisplayText();
        historyService.Changed += OnHistoryChanged;

        var selectedLangs = new HashSet<string>(settings.DictionaryLanguages);
        DictionaryLanguages = new ObservableCollection<DictionaryLanguageItem>(
            AppDefaults.DictionaryAvailableLanguages.Select(l =>
                new DictionaryLanguageItem(l.Code, l.Name, selectedLangs.Contains(l.Code))));
        foreach (var item in DictionaryLanguages)
            item.PropertyChanged += (_, e) => {
                if (e.PropertyName != nameof(DictionaryLanguageItem.IsSelected)) return;
                var selected = DictionaryLanguages.Where(l => l.IsSelected).Select(l => l.Code).ToList();
                if (selected.Count == 0) { item.IsSelected = true; return; }
                _settings.DictionaryLanguages = selected;
                _settings.Save();
                _logger.LogInformation("Settings: DictionaryLanguages = [{Languages}]", string.Join(", ", selected));
                _settings.NotifySearchSettingsChanged();
            };

        SearchFolders  = new ObservableCollection<SearchFolderItem>(settings.SearchFolders.Select(p => new SearchFolderItem(p)));
        AppDirectories = new ObservableCollection<string>(settings.AppDirectories);

        SearchFolders.CollectionChanged  += (_, e) => {
            settings.SearchFolders = SearchFolders.Select(f => f.RawPath).ToList();
            settings.Save();
            _logger.LogInformation("Settings: SearchFolders changed ({Action}) → {Count} folders", e.Action, SearchFolders.Count);
            OnPropertyChanged(nameof(HasSearchFolders));
            settings.NotifySearchSettingsChanged();
        };
        AppDirectories.CollectionChanged += (_, e) => {
            settings.AppDirectories = AppDirectories.ToList();
            settings.Save();
            _appDirectoriesDirty = true;
            _logger.LogInformation("Settings: AppDirectories changed ({Action}) → {Count} dirs", e.Action, AppDirectories.Count);
            OnPropertyChanged(nameof(HasMissingCommonAppDirectories));
            OnPropertyChanged(nameof(HasAppDirectories));
        };

        _showDisabledWebSearchEngines = settings.ShowDisabledWebSearchEngines;

        settings.EnsurePluginSettings(pluginService.Plugins);
        WebSearchGroups = BuildWebSearchGroups();
        pluginService.PluginsChanged += OnPluginsReloaded;
    }

    private void OnThemesChanged() {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            var currentId = SelectedTheme?.Id;
            Themes = _themeService.AvailableThemes();
            SelectedTheme = Themes.FirstOrDefault(t => t.Id == currentId)
                            ?? Themes.FirstOrDefault();
        });
    }

    private void OnPluginsReloaded() {
        _settings.EnsurePluginSettings(_pluginService.Plugins);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            WebSearchGroups = BuildWebSearchGroups();
        });
    }

    private List<WebSearchGroupViewModel> BuildWebSearchGroups() {
        var allRows = new List<WebSearchEngineRowViewModel>();

        // Built-in engines
        foreach (var engine in WebSearchDefaults.Engines) {
            var cfg = _settings.WebSearchEngines.FirstOrDefault(s => s.Id == engine.Id)
                      ?? WebSearchDefaults.DefaultSettingsFor(engine.Id);
            allRows.Add(new WebSearchEngineRowViewModel(
                engine.Id, engine.Name, engine.Group, engine.QueryUrl, engine.IconResource,
                cfg, _settings, _platform) { ShowDisabled = ShowDisabledWebSearchEngines });
        }

        // Plugin engines
        foreach (var plugin in _pluginService.Plugins) {
            var cfg = _settings.WebSearchEngines.FirstOrDefault(s => s.Id == plugin.Id);
            if (cfg == null) continue;
            var group = string.IsNullOrEmpty(plugin.Group) ? "general" : plugin.Group;
            allRows.Add(new WebSearchEngineRowViewModel(
                plugin.Id, plugin.Name, group, plugin.QueryUrl,
                _pluginService.GetIcon(plugin.Id), cfg, _settings, _platform,
                plugin.SourceFilePath) { ShowDisabled = ShowDisabledWebSearchEngines });
        }

        // Preserve group order: built-in groups first, then plugin-only groups
        var groupOrder = allRows.Select(r => r.Group).Distinct().ToList();
        return groupOrder
            .Select(g => new WebSearchGroupViewModel(g, allRows.Where(r => r.Group == g).ToList()))
            .ToList();
    }

    // ── Folder mutators (called from code-behind) ─────────────────────────────
    public void AddSearchFolder(string path) {
        var collapsed = PlatformProvider.CollapseHomePath(path.TrimEnd('/', '\\'));
        var item = new SearchFolderItem(collapsed);
        var expandedNew = item.DisplayPath;
        if (SearchFolders.All(f => f.DisplayPath != expandedNew))
            SearchFolders.Add(item);
    }

    public void RemoveSearchFolder(SearchFolderItem item) => SearchFolders.Remove(item);

    public void AddCommonFolders() {
        foreach (var raw in _platform.DefaultSearchFolders()) {
            var expanded = PlatformProvider.ExpandPath(raw);
            if (Directory.Exists(expanded) && SearchFolders.All(f => f.DisplayPath != expanded))
                SearchFolders.Add(new SearchFolderItem(raw));
        }
    }

    /// <summary>Called from code-behind when the browser ComboBox dropdown opens.</summary>
    public async Task RefreshBrowsersAsync() {
        if (IsBrowsersLoading) return;
        IsBrowsersLoading = true;
        var saved = SelectedBrowser;

        var discovered = await Task.Run(() => _browserDiscovery.Discover().Select(b => b.Name).ToList());

        if (!string.IsNullOrEmpty(_settings.Browser) && !discovered.Contains(_settings.Browser))
            discovered.Insert(0, _settings.Browser);

        Browsers = discovered;
        SelectedBrowser = Browsers.Contains(saved) ? saved : Browsers.FirstOrDefault();
        IsBrowsersLoading = false;
    }

    /// <summary>Called from code-behind when the terminal ComboBox dropdown opens.</summary>
    public async Task RefreshTerminalsAsync() {
        if (IsTerminalsLoading) return;
        IsTerminalsLoading = true;
        var saved = SelectedTerminal;

        var discovered = await Task.Run(() => _terminalDiscovery.Discover().Select(t => t.Name).ToList());

        if (!string.IsNullOrEmpty(_settings.Terminal) && !discovered.Contains(_settings.Terminal))
            discovered.Insert(0, _settings.Terminal);

        Terminals = discovered;
        SelectedTerminal = Terminals.Contains(saved) ? saved : Terminals.FirstOrDefault();
        IsTerminalsLoading = false;
    }

    public bool HasSearchFolders   => SearchFolders.Count > 0;
    public bool HasAppDirectories  => AppDirectories.Count > 0;

    public bool HasMissingCommonAppDirectories {
        get {
            var currentExpanded = AppDirectories
                .Select(p => PlatformProvider.ExpandPath(p.TrimEnd('/', '\\')))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _platform.DefaultAppDirectories().Any(raw => {
                var expanded = PlatformProvider.ExpandPath(raw.TrimEnd('/', '\\'));
                return Directory.Exists(expanded) && !currentExpanded.Contains(expanded);
            });
        }
    }                                                                   

    public void AddAppDirectory(string path) {
        var normalized = PlatformProvider.CollapseHomePath(path.TrimEnd('/', '\\'));
        var normalizedExpanded = PlatformProvider.ExpandPath(normalized);
        if (AppDirectories.All(d => PlatformProvider.ExpandPath(d.TrimEnd('/', '\\')) != normalizedExpanded))
            AppDirectories.Add(normalized);
    }

    public void RemoveAppDirectory(string path) => AppDirectories.Remove(path);

    public void AddCommonAppDirectories() {
        var currentExpanded = AppDirectories
            .Select(p => PlatformProvider.ExpandPath(p.TrimEnd('/', '\\')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in _platform.DefaultAppDirectories()) {
            var normalized = raw.TrimEnd('/', '\\');
            var expanded = PlatformProvider.ExpandPath(normalized);
            if (Directory.Exists(expanded) && !currentExpanded.Contains(expanded)) {
                AppDirectories.Add(normalized);
                currentExpanded.Add(expanded);
            }
        }
    }

    /// <summary>
    /// Flushes deferred AppDirectories changes: invalidates the app cache and
    /// browser/terminal discovery caches. Called when leaving the AppSearch section
    /// or when the settings window closes.
    /// </summary>
    public void FlushAppDirectoryChanges() {
        if (!_appDirectoriesDirty) return;
        _appDirectoriesDirty = false;
        _settings.NotifyAppDirectoriesChanged();
        _browserDiscovery.InvalidateCache();
        _terminalDiscovery.InvalidateCache();
        _settings.NotifySearchSettingsChanged();
    }

    /// <summary>Called from the View when the settings window is closed.</summary>
    public void OnWindowClosed() {
        FlushAppDirectoryChanges();
        _logger.LogInformation("Settings: closed");
    }

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
        _logger.LogInformation("Settings: Hotkey = \"{Value}\"", config);
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
        _logger.LogInformation("Settings: Browser = \"{Value}\"", value);
    }

    partial void OnSelectedTerminalChanged(string? value) {
        _settings.Terminal = value ?? "";
        _settings.Save();
        _logger.LogInformation("Settings: Terminal = \"{Value}\"", value);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value) {
        if (value is null) return;
        _settings.Theme = value.Id;
        _settings.Save();
        _logger.LogInformation("Settings: Theme = \"{Value}\"", value.Id);
        _themeService.Apply(value.Id);
    }

    private void OnHistoryChanged() {
        HistoryDisplayText = BuildHistoryDisplayText();
    }

    private string BuildHistoryDisplayText() {
        if (_historyService.Entries.Count == 0) return "(empty)";
        return string.Join("\n", _historyService.Entries
            .AsEnumerable()
            .Reverse()
            .Select(e => {
                var ts = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                var action = e.ActionName != null ? $" → {e.ActionName}" : "";
                return $"[{ts}] \"{e.Query}\"{action}";
            }));
    }

    [RelayCommand]
    private void ClearHistory() {
        _historyService.Clear();
    }
}

public partial class DictionaryLanguageItem : ObservableObject {
    public string Code { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    public DictionaryLanguageItem(string code, string name, bool isSelected) {
        Code = code;
        Name = name;
        _isSelected = isSelected;
    }
}

public record SearchFolderItem {
    public string RawPath { get; }
    public string DisplayPath { get; }
    public bool Exists { get; }
    public bool DoesNotExist => !Exists;

    public SearchFolderItem(string rawPath) {
        RawPath = rawPath.TrimEnd('/', '\\');
        DisplayPath = PlatformProvider.ExpandPath(RawPath);
        Exists = Directory.Exists(DisplayPath);
    }
}

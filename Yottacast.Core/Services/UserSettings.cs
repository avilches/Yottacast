using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.WebSearch;

namespace Yottacast.Core.Services;

public class UserSettings {
    private readonly PlatformProvider _platform;
    private readonly ILogger<UserSettings>? _logger;
    private readonly string _settingsPath;

    private UserSettings(PlatformProvider platform, ILogger<UserSettings>? logger = null, string? settingsPath = null) {
        _platform = platform;
        _logger = logger;
        _settingsPath = settingsPath ?? DefaultSettingsPath;
    }

    public string Browser { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Theme { get; set; } = "dark-default";
    public List<string> SearchFolders { get; set; } = [];
    public List<string> AppDirectories { get; set; } = [];

    public event Action? AppDirectoriesChanged;
    public void NotifyAppDirectoriesChanged() => AppDirectoriesChanged?.Invoke();

    public event Action? SearchSettingsChanged;
    public void NotifySearchSettingsChanged() => SearchSettingsChanged?.Invoke();

    public event Action? StickyWindowChanged;
    public void NotifyStickyWindowChanged() => StickyWindowChanged?.Invoke();
    public bool EnableAppSearch { get; set; } = true;
    public bool EnableCalculator { get; set; } = true;
    public bool EnableEmoji { get; set; } = true;
    public SearchSourceVisibility FileSearchVisibility { get; set; } = SearchSourceVisibility.Always;
    public SearchSourceVisibility ClipboardSearchVisibility { get; set; } = SearchSourceVisibility.Disabled;

    private string? _clipboardHotkey;
    private HotkeyConfig? _parsedClipboardHotkey;

    public string? ClipboardHotkey {
        get => _clipboardHotkey;
        set { _clipboardHotkey = value; _parsedClipboardHotkey = null; }
    }

    public HotkeyConfig? ParsedClipboardHotkey {
        get {
            if (_clipboardHotkey is null) return null;
            return _parsedClipboardHotkey ??= HotkeyConfig.Parse(_clipboardHotkey);
        }
    }
    public bool EnableWebSearch { get; set; } = true;
    public bool EnableUrlValidation { get; set; } = true;
    public bool ShowDisabledWebSearchEngines { get; set; } = true;
    public bool FileSearchOnlySpecificFolders { get; set; } = false;
    public string LastLaunchedVersion { get; set; } = "";
    public List<WebSearchEngineSettings> WebSearchEngines { get; set; } = [];
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool StickyWindow { get; set; } = true;
    public string CalculatorCurrencyA { get; set; } = "EUR";
    public string CalculatorCurrencyB { get; set; } = "USD";
    public int CalculatorDecimalPlaces { get; set; } = 2;
    public bool CalculatorIncludeMetals { get; set; } = true;
    public bool CalculatorIncludeCrypto { get; set; } = false;
    public int ExchangeRateRefreshIntervalHours { get; set; } = AppDefaults.ExchangeRateRefreshIntervalHours;
    public bool EnableDictionary { get; set; } = true;
    public string DictionaryPrefix { get; set; } = AppDefaults.DictionaryDefaultPrefix;
    public bool EnableSystemSettings { get; set; } = true;
    public bool DictionaryShowAlways { get; set; } = false;
    public List<string> DictionaryLanguages { get; set; } = new(AppDefaults.DictionaryDefaultLanguages);
    public bool DateSearchEnabled { get; set; } = true;
    public List<string> DateSearchLanguages { get; set; } = new(AppDefaults.DateSearchDefaultLanguages);
    public string DateIsoFormat { get; set; } = AppDefaults.DateIsoFormat;
    public string DateLongFormat { get; set; } = AppDefaults.DateLongFormat;
    public bool EnableHistory { get; set; } = true;
    public int HistoryMaxItems { get; set; } = AppDefaults.HistoryMaxItems;
    public int ClipboardHistoryMaxEntries { get; set; } = AppDefaults.ClipboardHistoryMaxEntries;
    public int ClipboardHistoryMaxDays { get; set; } = AppDefaults.ClipboardHistoryMaxDays;
    public bool EnableFileEditor { get; set; } = true;
    public bool FileEditorAutoSave { get; set; } = false;
    public List<string> FileEditorExtensions { get; set; } = [..AppDefaults.FileEditorDefaultExtensions];
    public bool KeepValueWhenHide { get; set; } = true;
    public int KeepValueWhenHideDuration { get; set; } = AppDefaults.KeepValueWhenHideDuration;

    private string _hotkey = "Alt+Space";
    private HotkeyConfig? _parsedHotkey;

    public string Hotkey {
        get => _hotkey;
        set {
            _hotkey = value;
            _parsedHotkey = null;
        }
    }

    public HotkeyConfig ParsedHotkey => _parsedHotkey ??= HotkeyConfig.Parse(_hotkey) ?? HotkeyConfig.Default;

    /// <summary>Raw SearchFolders with $HOME/~ expanded to absolute paths, for use in file searches.</summary>
    public IReadOnlyList<string> ExpandedSearchFolders => SearchFolders.Select(PlatformProvider.ExpandPath).ToList();

    /// <summary>Raw AppDirectories with $HOME/~ expanded to absolute paths, for use in app scanning.</summary>
    public IReadOnlyList<string> ExpandedAppDirectories => AppDirectories.Select(PlatformProvider.ExpandPath).ToList();

    /// <summary>
    /// Checks that the stored Browser and Terminal still exist on disk.
    /// If not, self-heals by picking the first available and saving.
    /// Call this at natural access points (e.g. when Settings opens).
    /// </summary>
    public void EnsureIntegrity() {
        _ = ActiveBrowser;
        _ = ActiveTerminal;
    }

    /// <summary>
    /// Resolves the preferred browser from disk. If the stored name no longer exists,
    /// falls back to the first available, updates Browser, and saves.
    /// Returns null if there is no available browser (neither the configured one nor the default ones)
    /// </summary>
    public BrowserInfo? ActiveBrowser {
        get {
            var resolved = BrowserDiscovery.Resolve(Browser, _platform, ExpandedAppDirectories);
            if (resolved is not null && resolved.Name != Browser) {
                _logger?.LogInformation("Settings: browser '{OldBrowser}' not found, switching to '{NewBrowser}'", Browser, resolved.Name);
                Browser = resolved.Name;
                Save();
            }
            return resolved;
        }
    }

    /// <summary>
    /// Resolves the preferred terminal from disk. If the stored name no longer exists,
    /// falls back to the first available, updates Terminal, and saves.
    /// Returns null if there is no available terminal (neither the configured one nor the default ones)
    /// </summary>
    public TerminalInfo? ActiveTerminal {
        get {
            var resolved = TerminalDiscovery.Resolve(Terminal, _platform, ExpandedAppDirectories);
            if (resolved is not null && resolved.Name != Terminal) {
                _logger?.LogInformation("Settings: terminal '{OldTerminal}' not found, switching to '{NewTerminal}'", Terminal, resolved.Name);
                Terminal = resolved.Name;
                Save();
            }
            return resolved;
        }
    }

    private static readonly string DefaultSettingsPath = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private record WebSearchEngineSettingsData {
        [JsonPropertyName("id")]      public string Id      { get; init; } = "";
        [JsonPropertyName("enabled")] public bool Enabled   { get; init; } = true;
        [JsonPropertyName("mode")]    public string Mode    { get; init; } = "";
        [JsonPropertyName("prefix")]  public string Prefix  { get; init; } = "";
        [JsonPropertyName("queryUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? QueryUrl { get; init; }
    }

    private record UserSettingsData {
        [JsonPropertyName("browser")] public string Browser { get; init; } = "";
        [JsonPropertyName("terminal")] public string Terminal { get; init; } = "";
        [JsonPropertyName("theme")] public string Theme { get; init; } = "";
        [JsonPropertyName("hotkey")] public string Hotkey { get; init; } = "Alt+Space";
        [JsonPropertyName("searchFolders")] public List<string>? SearchFolders { get; init; }
        [JsonPropertyName("appDirectories")] public List<string>? AppDirectories { get; init; }
        [JsonPropertyName("enableAppSearch")] public bool EnableAppSearch { get; init; } = true;
        [JsonPropertyName("enableCalculator")] public bool EnableCalculator { get; init; } = true;
        [JsonPropertyName("enableConverter")] public bool EnableConverter { get; init; } = false;
        [JsonPropertyName("enableClipboard")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ClipboardHistoryEnabled { get; init; } = false;
        [JsonPropertyName("clipboardHistoryMaxEntries")] public int ClipboardHistoryMaxEntries { get; init; } = AppDefaults.ClipboardHistoryMaxEntries;
        [JsonPropertyName("clipboardHistoryMaxDays")] public int ClipboardHistoryMaxDays { get; init; } = AppDefaults.ClipboardHistoryMaxDays;
        [JsonPropertyName("enableEmoji")] public bool EnableEmoji { get; init; } = true;
        [JsonPropertyName("enableFileSearch")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableFileSearch { get; init; }  // solo para migración; null en ficheros nuevos
        [JsonPropertyName("fileSearchVisibility")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FileSearchVisibility { get; init; }
        [JsonPropertyName("clipboardSearchVisibility")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClipboardSearchVisibility { get; init; }
        [JsonPropertyName("clipboardHotkey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClipboardHotkey { get; init; }
        [JsonPropertyName("enableWebSearch")] public bool EnableWebSearch { get; init; } = true;
        [JsonPropertyName("enableUrlValidation")] public bool EnableUrlValidation { get; init; } = true;
        [JsonPropertyName("showDisabledWebSearchEngines")] public bool ShowDisabledWebSearchEngines { get; init; } = true;
        [JsonPropertyName("fileSearchOnlySpecificFolders")] public bool FileSearchOnlySpecificFolders { get; init; } = false;
        [JsonPropertyName("lastLaunchedVersion")] public string LastLaunchedVersion { get; init; } = "";
        [JsonPropertyName("webSearchEngines")] public List<WebSearchEngineSettingsData>? WebSearchEngines { get; init; }
        [JsonPropertyName("windowX")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WindowX { get; init; }
        [JsonPropertyName("windowY")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? WindowY { get; init; }
        [JsonPropertyName("stickyWindow")] public bool StickyWindow { get; init; } = true;
        [JsonPropertyName("calculatorCurrencyA")] public string CalculatorCurrencyA { get; init; } = "EUR";
        [JsonPropertyName("calculatorCurrencyB")] public string CalculatorCurrencyB { get; init; } = "USD";
        [JsonPropertyName("calculatorDecimalPlaces")] public int CalculatorDecimalPlaces { get; init; } = 2;
        [JsonPropertyName("calculatorIncludeMetals")] public bool CalculatorIncludeMetals { get; init; } = true;
        [JsonPropertyName("calculatorIncludeCrypto")] public bool CalculatorIncludeCrypto { get; init; } = false;
        [JsonPropertyName("exchangeRateRefreshIntervalHours")] public int ExchangeRateRefreshIntervalHours { get; init; } = AppDefaults.ExchangeRateRefreshIntervalHours;
        [JsonPropertyName("enableDictionary")] public bool EnableDictionary { get; init; } = true;
        [JsonPropertyName("dictionaryPrefix")] public string DictionaryPrefix { get; init; } = AppDefaults.DictionaryDefaultPrefix;
        [JsonPropertyName("dictionaryShowAlways")] public bool DictionaryShowAlways { get; init; } = false;
        [JsonPropertyName("enableSystemSettings")] public bool EnableSystemSettings { get; init; } = true;
        [JsonPropertyName("dictionaryLanguages")] public List<string>? DictionaryLanguages { get; init; }
        [JsonPropertyName("dateSearchEnabled")] public bool DateSearchEnabled { get; init; } = true;
        [JsonPropertyName("dateSearchLanguages")] public List<string>? DateSearchLanguages { get; init; }
        [JsonPropertyName("dateIsoFormat")] public string DateIsoFormat { get; init; } = AppDefaults.DateIsoFormat;
        [JsonPropertyName("dateLongFormat")] public string DateLongFormat { get; init; } = AppDefaults.DateLongFormat;
        [JsonPropertyName("enableHistory")] public bool EnableHistory { get; init; } = true;
        [JsonPropertyName("historyMaxItems")] public int HistoryMaxItems { get; init; } = AppDefaults.HistoryMaxItems;
        [JsonPropertyName("keepValueWhenHide")] public bool KeepValueWhenHide { get; init; } = true;
        [JsonPropertyName("keepValueWhenHideDuration")] public int KeepValueWhenHideDuration { get; init; } = AppDefaults.KeepValueWhenHideDuration;
        [JsonPropertyName("enableFileEditor")] public bool EnableFileEditor { get; init; } = true;
        [JsonPropertyName("fileEditorAutoSave")] public bool FileEditorAutoSave { get; init; } = false;
        [JsonPropertyName("fileEditorExtensions")] public List<string>? FileEditorExtensions { get; init; }
    }

    public static UserSettings Load(PlatformProvider platform, ILogger<UserSettings>? logger = null, string? settingsPath = null) {
        var path = settingsPath ?? DefaultSettingsPath;
        UserSettings settings;
        try {
            if (!File.Exists(path)) {
                throw new FileNotFoundException($"Settings file '{path}' does not exist");
            }
            logger?.LogInformation("Settings loaded from {Path}", path);
            var data = JsonSerializer.Deserialize<UserSettingsData>(File.ReadAllText(path), JsonOptions);
            if (data == null) {
                settings = CreateDefaultUserSettings(platform, logger, path);
            } else {
                settings = new UserSettings(platform, logger, path) {
                    Browser = data.Browser,
                    Terminal = data.Terminal,
                    Theme = string.IsNullOrEmpty(data.Theme) ? platform.DefaultTheme() : data.Theme,
                    Hotkey = string.IsNullOrEmpty(data.Hotkey) ? "Alt+Space" : data.Hotkey,
                    EnableAppSearch = data.EnableAppSearch,
                    FileSearchVisibility = data.FileSearchVisibility != null
                        ? Enum.TryParse<SearchSourceVisibility>(data.FileSearchVisibility, ignoreCase: true, out var fsv)
                            ? fsv : SearchSourceVisibility.Always
                        : data.EnableFileSearch == false
                            ? SearchSourceVisibility.Disabled
                            : SearchSourceVisibility.Always,
                    ClipboardSearchVisibility = data.ClipboardSearchVisibility != null
                        ? Enum.TryParse<SearchSourceVisibility>(data.ClipboardSearchVisibility, ignoreCase: true, out var csv)
                            ? csv : SearchSourceVisibility.Disabled
                        : SearchSourceVisibility.Disabled,
                    ClipboardHotkey = data.ClipboardHotkey,
                    EnableWebSearch = data.EnableWebSearch,
                    EnableUrlValidation = data.EnableUrlValidation,
                    ShowDisabledWebSearchEngines = data.ShowDisabledWebSearchEngines,
                    FileSearchOnlySpecificFolders = data.FileSearchOnlySpecificFolders,
                    SearchFolders = (data.SearchFolders?.Count > 0
                            ? data.SearchFolders
                            : platform.DefaultSearchFolders()
                                .Where(f => Directory.Exists(PlatformProvider.ExpandPath(f)))
                                .ToList())
                        .Select(p => p.TrimEnd('/', '\\'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    AppDirectories = (data.AppDirectories?.Count > 0
                            ? data.AppDirectories
                            : platform.DefaultAppDirectories())
                        .Select(p => p.TrimEnd('/', '\\'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    EnableCalculator = data.EnableCalculator || data.EnableConverter,
                    ClipboardHistoryMaxEntries = data.ClipboardHistoryMaxEntries,
                    ClipboardHistoryMaxDays = data.ClipboardHistoryMaxDays,
                    EnableEmoji = data.EnableEmoji,
                    LastLaunchedVersion = data.LastLaunchedVersion,
                    WebSearchEngines = MergeWebSearchEngines(data.WebSearchEngines),
                    WindowX = data.WindowX,
                    WindowY = data.WindowY,
                    StickyWindow = data.StickyWindow,
                    CalculatorCurrencyA = string.IsNullOrWhiteSpace(data.CalculatorCurrencyA) ? "EUR" : data.CalculatorCurrencyA.ToUpperInvariant(),
                    CalculatorCurrencyB = string.IsNullOrWhiteSpace(data.CalculatorCurrencyB) ? "USD" : data.CalculatorCurrencyB.ToUpperInvariant(),
                    CalculatorDecimalPlaces = data.CalculatorDecimalPlaces is >= 0 and <= 10 ? data.CalculatorDecimalPlaces : 2,
                    CalculatorIncludeMetals = data.CalculatorIncludeMetals,
                    CalculatorIncludeCrypto = data.CalculatorIncludeCrypto,
                    ExchangeRateRefreshIntervalHours = data.ExchangeRateRefreshIntervalHours is >= 1 and <= 168 ? data.ExchangeRateRefreshIntervalHours : AppDefaults.ExchangeRateRefreshIntervalHours,
                    EnableDictionary = data.EnableDictionary,
                    DictionaryPrefix = string.IsNullOrWhiteSpace(data.DictionaryPrefix) ? AppDefaults.DictionaryDefaultPrefix : data.DictionaryPrefix,
                    DictionaryShowAlways = data.DictionaryShowAlways,
                    DictionaryLanguages = data.DictionaryLanguages is { Count: > 0 } ? data.DictionaryLanguages : new(AppDefaults.DictionaryDefaultLanguages),
                    DateSearchEnabled = data.DateSearchEnabled,
                    DateSearchLanguages = data.DateSearchLanguages is { Count: > 0 } ? data.DateSearchLanguages : new(AppDefaults.DateSearchDefaultLanguages),
                    DateIsoFormat = string.IsNullOrWhiteSpace(data.DateIsoFormat) ? AppDefaults.DateIsoFormat : data.DateIsoFormat,
                    DateLongFormat = string.IsNullOrWhiteSpace(data.DateLongFormat) ? AppDefaults.DateLongFormat : data.DateLongFormat,
                    EnableHistory = data.EnableHistory,
                    HistoryMaxItems = data.HistoryMaxItems is >= 1 and <= AppDefaults.HistoryMaxItems
                        ? data.HistoryMaxItems
                        : AppDefaults.HistoryMaxItems,
                    KeepValueWhenHide = data.KeepValueWhenHide,
                    KeepValueWhenHideDuration = data.KeepValueWhenHideDuration >= 0
                        ? data.KeepValueWhenHideDuration
                        : AppDefaults.KeepValueWhenHideDuration,
                    EnableFileEditor = data.EnableFileEditor,
                    FileEditorAutoSave = data.FileEditorAutoSave,
                    FileEditorExtensions = data.FileEditorExtensions is { Count: > 0 }
                        ? data.FileEditorExtensions
                        : [..AppDefaults.FileEditorDefaultExtensions],
                    EnableSystemSettings = data.EnableSystemSettings,
                };
            }
        } catch (Exception ex) {
            logger?.LogInformation("Settings not found or invalid ({Message}), creating defaults at {Path}", ex.Message, path);
            settings = CreateDefaultUserSettings(platform, logger, path);
        }
        settings.Save();
        return settings;
    }

    /// <summary>
    /// Merges saved engine settings with the hardcoded defaults.
    /// Engines present in the defaults but missing from saved settings are added with their defaults,
    /// so newly added engines appear automatically for existing users.
    /// </summary>
    private static WebSearchMode ParseMode(string? mode) =>
        Enum.TryParse<WebSearchMode>(mode, ignoreCase: true, out var result) ? result : WebSearchMode.PrefixOnly;

    private static List<WebSearchEngineSettings> MergeWebSearchEngines(List<WebSearchEngineSettingsData>? saved) {
        var savedById = (saved ?? [])
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .ToDictionary(s => s.Id);

        return WebSearchDefaults.Engines.Select(engine =>
            savedById.TryGetValue(engine.Id, out var s)
                ? new WebSearchEngineSettings {
                    Id = s.Id,
                    Enabled = s.Enabled,
                    Mode = ParseMode(s.Mode),
                    Prefix = s.Prefix,
                    QueryUrl = string.IsNullOrEmpty(s.QueryUrl) ? null : s.QueryUrl,
                }
                : WebSearchDefaults.DefaultSettingsFor(engine.Id)
        ).ToList();
    }

    /// <summary>
    /// Ensures every loaded plugin has a matching entry in WebSearchEngines.
    /// New plugins get defaults (Enabled=true, PrefixOnly, prefix from plugin).
    /// Persists only if new entries were added.
    /// </summary>
    public void EnsurePluginSettings(IReadOnlyList<WebSearchPlugin> plugins) {
        var existingIds = new HashSet<string>(WebSearchEngines.Select(e => e.Id));
        var added = false;
        foreach (var plugin in plugins) {
            if (existingIds.Contains(plugin.Id)) continue;
            WebSearchEngines.Add(new WebSearchEngineSettings {
                Id      = plugin.Id,
                Enabled = true,
                Mode    = WebSearchMode.PrefixOnly,
                Prefix  = plugin.DefaultPrefix,
            });
            added = true;
        }
        if (added) Save();
    }

    private static UserSettings CreateDefaultUserSettings(PlatformProvider platform, ILogger<UserSettings>? logger, string? settingsPath = null) {
        return new UserSettings(platform, logger, settingsPath) {
            Theme = platform.DefaultTheme(),
            SearchFolders = platform.DefaultSearchFolders()
                .Where(f => Directory.Exists(PlatformProvider.ExpandPath(f)))
                .Select(p => p.TrimEnd('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AppDirectories = platform.DefaultAppDirectories()
                .Select(p => p.TrimEnd('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WebSearchEngines = MergeWebSearchEngines(null),
        };
    }

    public void Save() {
        try {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            var data = new UserSettingsData {
                Browser = Browser,
                Terminal = Terminal,
                Theme = Theme,
                Hotkey = Hotkey,
                SearchFolders = SearchFolders,
                AppDirectories = AppDirectories,
                EnableAppSearch = EnableAppSearch,
                EnableCalculator = EnableCalculator,
                ClipboardHistoryMaxEntries = ClipboardHistoryMaxEntries,
                ClipboardHistoryMaxDays = ClipboardHistoryMaxDays,
                EnableEmoji = EnableEmoji,
                FileSearchVisibility = FileSearchVisibility.ToString(),
                ClipboardSearchVisibility = ClipboardSearchVisibility.ToString(),
                ClipboardHotkey = ClipboardHotkey,
                EnableWebSearch = EnableWebSearch,
                EnableUrlValidation = EnableUrlValidation,
                ShowDisabledWebSearchEngines = ShowDisabledWebSearchEngines,
                FileSearchOnlySpecificFolders = FileSearchOnlySpecificFolders,
                LastLaunchedVersion = LastLaunchedVersion,
                WindowX = WindowX,
                WindowY = WindowY,
                StickyWindow = StickyWindow,
                CalculatorCurrencyA = CalculatorCurrencyA,
                CalculatorCurrencyB = CalculatorCurrencyB,
                CalculatorDecimalPlaces = CalculatorDecimalPlaces,
                CalculatorIncludeMetals = CalculatorIncludeMetals,
                CalculatorIncludeCrypto = CalculatorIncludeCrypto,
                ExchangeRateRefreshIntervalHours = ExchangeRateRefreshIntervalHours,
                EnableDictionary = EnableDictionary,
                DictionaryPrefix = DictionaryPrefix,
                DictionaryShowAlways = DictionaryShowAlways,
                DictionaryLanguages = DictionaryLanguages,
                DateSearchEnabled = DateSearchEnabled,
                DateSearchLanguages = DateSearchLanguages,
                DateIsoFormat = DateIsoFormat,
                DateLongFormat = DateLongFormat,
                EnableHistory = EnableHistory,
                HistoryMaxItems = HistoryMaxItems,
                KeepValueWhenHide = KeepValueWhenHide,
                KeepValueWhenHideDuration = KeepValueWhenHideDuration,
                EnableFileEditor = EnableFileEditor,
                FileEditorAutoSave = FileEditorAutoSave,
                FileEditorExtensions = FileEditorExtensions,
                EnableSystemSettings = EnableSystemSettings,
                WebSearchEngines = WebSearchEngines
                    .Select(s => new WebSearchEngineSettingsData {
                        Id = s.Id,
                        Enabled = s.Enabled,
                        Mode = s.Mode.ToString(),
                        Prefix = s.Prefix,
                        QueryUrl = string.IsNullOrEmpty(s.QueryUrl) ? null : s.QueryUrl,
                    })
                    .ToList(),
            };
            Directory.CreateDirectory(dir);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data, JsonOptions));
            _logger?.LogDebug("Settings saved to {Path}", _settingsPath);
        } catch (Exception ex) {
            _logger?.LogWarning("Settings save error: {Message}", ex.Message);
        }
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
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
    public bool EnableAppSearch { get; set; } = true;
    public bool EnableCalculator { get; set; } = true;
    public bool EnableClipboard { get; set; } = true;
    public bool EnableEmoji { get; set; } = true;
    public bool EnableFileSearch { get; set; } = true;
    public bool FileSearchOnlySpecificFolders { get; set; } = false;
    public string LastLaunchedVersion { get; set; } = "";
    public List<WebSearchEngineSettings> WebSearchEngines { get; set; } = [];
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool StickyWindow { get; set; } = true;
    public string CalculatorCurrencyA { get; set; } = "EUR";
    public string CalculatorCurrencyB { get; set; } = "USD";
    public int CalculatorDecimalPlaces { get; set; } = 2;
    public bool EnableDictionary { get; set; } = true;
    public string DictionaryPrefix { get; set; } = AppDefaults.DictionaryDefaultPrefix;
    public bool DictionaryShowAlways { get; set; } = false;

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
            var resolved = BrowserDiscovery.Resolve(Browser, _platform);
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
            var resolved = TerminalDiscovery.Resolve(Terminal, _platform);
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
        [JsonPropertyName("enableClipboard")] public bool EnableClipboard { get; init; } = true;
        [JsonPropertyName("enableEmoji")] public bool EnableEmoji { get; init; } = true;
        [JsonPropertyName("enableFileSearch")] public bool EnableFileSearch { get; init; } = true;
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
        [JsonPropertyName("enableDictionary")] public bool EnableDictionary { get; init; } = true;
        [JsonPropertyName("dictionaryPrefix")] public string DictionaryPrefix { get; init; } = AppDefaults.DictionaryDefaultPrefix;
        [JsonPropertyName("dictionaryShowAlways")] public bool DictionaryShowAlways { get; init; } = false;
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
                    EnableFileSearch = data.EnableFileSearch,
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
                    EnableCalculator = data.EnableCalculator,
                    EnableClipboard = data.EnableClipboard,
                    EnableEmoji = data.EnableEmoji,
                    LastLaunchedVersion = data.LastLaunchedVersion,
                    WebSearchEngines = MergeWebSearchEngines(data.WebSearchEngines),
                    WindowX = data.WindowX,
                    WindowY = data.WindowY,
                    StickyWindow = data.StickyWindow,
                    CalculatorCurrencyA = string.IsNullOrWhiteSpace(data.CalculatorCurrencyA) ? "EUR" : data.CalculatorCurrencyA.ToUpperInvariant(),
                    CalculatorCurrencyB = string.IsNullOrWhiteSpace(data.CalculatorCurrencyB) ? "USD" : data.CalculatorCurrencyB.ToUpperInvariant(),
                    CalculatorDecimalPlaces = data.CalculatorDecimalPlaces is >= 0 and <= 10 ? data.CalculatorDecimalPlaces : 2,
                    EnableDictionary = data.EnableDictionary,
                    DictionaryPrefix = string.IsNullOrWhiteSpace(data.DictionaryPrefix) ? AppDefaults.DictionaryDefaultPrefix : data.DictionaryPrefix,
                    DictionaryShowAlways = data.DictionaryShowAlways,
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
                EnableClipboard = EnableClipboard,
                EnableEmoji = EnableEmoji,
                EnableFileSearch = EnableFileSearch,
                FileSearchOnlySpecificFolders = FileSearchOnlySpecificFolders,
                LastLaunchedVersion = LastLaunchedVersion,
                WindowX = WindowX,
                WindowY = WindowY,
                StickyWindow = StickyWindow,
                CalculatorCurrencyA = CalculatorCurrencyA,
                CalculatorCurrencyB = CalculatorCurrencyB,
                CalculatorDecimalPlaces = CalculatorDecimalPlaces,
                EnableDictionary = EnableDictionary,
                DictionaryPrefix = DictionaryPrefix,
                DictionaryShowAlways = DictionaryShowAlways,
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
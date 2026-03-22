using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

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
    public bool EnableCalculator { get; set; } = true;
    public bool EnableClipboard { get; set; } = true;
    public bool EnableEmoji { get; set; } = true;
    public string LastLaunchedVersion { get; set; } = "";

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

    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yottacast", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private record UserSettingsData {
        [JsonPropertyName("browser")] public string Browser { get; init; } = "";
        [JsonPropertyName("terminal")] public string Terminal { get; init; } = "";
        [JsonPropertyName("theme")] public string Theme { get; init; } = "";
        [JsonPropertyName("hotkey")] public string Hotkey { get; init; } = "Alt+Space";
        [JsonPropertyName("searchFolders")] public List<string>? SearchFolders { get; init; }
        [JsonPropertyName("appDirectories")] public List<string>? AppDirectories { get; init; }
        [JsonPropertyName("enableCalculator")] public bool EnableCalculator { get; init; } = true;
        [JsonPropertyName("enableClipboard")] public bool EnableClipboard { get; init; } = true;
        [JsonPropertyName("enableEmoji")] public bool EnableEmoji { get; init; } = true;
        [JsonPropertyName("lastLaunchedVersion")] public string LastLaunchedVersion { get; init; } = "";
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
                    SearchFolders = data.SearchFolders?.Count > 0 ? data.SearchFolders : platform.DefaultSearchFolders(),
                    AppDirectories = data.AppDirectories?.Count > 0 ? data.AppDirectories : platform.DefaultAppDirectories(),
                    EnableCalculator = data.EnableCalculator,
                    EnableClipboard = data.EnableClipboard,
                    EnableEmoji = data.EnableEmoji,
                    LastLaunchedVersion = data.LastLaunchedVersion,
                };
            }
        } catch (Exception ex) {
            logger?.LogInformation("Settings not found or invalid ({Message}), creating defaults at {Path}", ex.Message, path);
            settings = CreateDefaultUserSettings(platform, logger, path);
        }
        settings.Save();
        return settings;
    }

    private static UserSettings CreateDefaultUserSettings(PlatformProvider platform, ILogger<UserSettings>? logger, string? settingsPath = null) {
        return new UserSettings(platform, logger, settingsPath) {
            Theme = platform.DefaultTheme(),
            SearchFolders = platform.DefaultSearchFolders(),
            AppDirectories = platform.DefaultAppDirectories(),
        };
    }

    public void Save() {
        try {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            var data = new UserSettingsData {
                Browser = Browser,
                Terminal = Terminal,
                Theme = Theme,
                Hotkey = Hotkey,
                SearchFolders = SearchFolders,
                AppDirectories = AppDirectories,
                EnableCalculator = EnableCalculator,
                EnableClipboard = EnableClipboard,
                EnableEmoji = EnableEmoji,
                LastLaunchedVersion = LastLaunchedVersion,
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data, JsonOptions));
            _logger?.LogDebug("Settings saved to {Path}", _settingsPath);
        } catch (Exception ex) {
            _logger?.LogWarning("Settings save error: {Message}", ex.Message);
        }
    }
}
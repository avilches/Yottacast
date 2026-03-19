using System.Text.Json;
using System.Text.Json.Nodes;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

public class UserSettings {
    private readonly PlatformProvider _platform;

    private UserSettings(PlatformProvider platform) {
        _platform = platform;
    }

    public string Browser  { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Theme    { get; set; } = "dark-default";
    public List<string> SearchFolders  { get; set; } = [];
    public List<string> AppDirectories { get; set; } = [];

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
    /// </summary>
    public BrowserInfo? ActiveBrowser {
        get {
            var resolved = BrowserDiscovery.Resolve(Browser, _platform);
            if (!string.IsNullOrEmpty(Browser) && resolved is not null && resolved.Name != Browser) {
                Console.WriteLine($"[Settings] Browser '{Browser}' not found, switching to '{resolved.Name}'");
                Browser = resolved.Name;
                Save();
            }
            return resolved;
        }
    }

    /// <summary>
    /// Resolves the preferred terminal from disk. If the stored name no longer exists,
    /// falls back to the first available, updates Terminal, and saves.
    /// </summary>
    public TerminalInfo? ActiveTerminal {
        get {
            var resolved = TerminalDiscovery.Resolve(Terminal, _platform);
            if (!string.IsNullOrEmpty(Terminal) && resolved is not null && resolved.Name != Terminal) {
                Console.WriteLine($"[Settings] Terminal '{Terminal}' not found, switching to '{resolved.Name}'");
                Terminal = resolved.Name;
                Save();
            }
            return resolved;
        }
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yottacast", "settings.json");

    public static UserSettings Load(PlatformProvider platform) {
        try {
            if (!File.Exists(SettingsPath)) {
                return new UserSettings(platform) {
                    Theme          = platform.DefaultTheme(),
                    SearchFolders  = platform.DefaultSearchFolders(),
                    AppDirectories = platform.DefaultAppDirectories(),
                };
            }
            var json = JsonNode.Parse(File.ReadAllText(SettingsPath));
            if (json == null) {
                return new UserSettings(platform) {
                    Theme          = platform.DefaultTheme(),
                    SearchFolders  = platform.DefaultSearchFolders(),
                    AppDirectories = platform.DefaultAppDirectories(),
                };
            }

            var folders = json["searchFolders"]?.AsArray()
                .Select(n => n?.GetValue<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();

            var appDirs = json["appDirectories"]?.AsArray()
                .Select(n => n?.GetValue<string>())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();

            // Theme auto-detected from OS when the key is absent from JSON
            var themeNode = json["theme"];
            var theme = themeNode is not null
                ? themeNode.GetValue<string>()
                : platform.DefaultTheme();

            return new UserSettings(platform) {
                Browser        = json["browser"]?.GetValue<string>()  ?? "",
                Terminal       = json["terminal"]?.GetValue<string>() ?? "",
                Theme          = theme,
                SearchFolders  = folders?.Count > 0 ? folders : platform.DefaultSearchFolders(),
                AppDirectories = appDirs?.Count > 0 ? appDirs : platform.DefaultAppDirectories(),
            };
        } catch {
            return new UserSettings(platform) {
                Theme          = platform.DefaultTheme(),
                SearchFolders  = platform.DefaultSearchFolders(),
                AppDirectories = platform.DefaultAppDirectories(),
            };
        }
    }

    public void Save() {
        try {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var foldersArray = new JsonArray();
            foreach (var f in SearchFolders) foldersArray.Add(f);
            var appDirsArray = new JsonArray();
            foreach (var d in AppDirectories) appDirsArray.Add(d);
            var json = new JsonObject {
                ["browser"]        = Browser,
                ["terminal"]       = Terminal,
                ["theme"]          = Theme,
                ["searchFolders"]  = foldersArray,
                ["appDirectories"] = appDirsArray,
            };
            File.WriteAllText(SettingsPath,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) {
            Console.WriteLine($"[Settings] Save error: {ex.Message}");
        }
    }
}

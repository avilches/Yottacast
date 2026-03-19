using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yottacast.Core.Services;

public class UserSettings {
    public string Browser  { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Theme    { get; set; } = "dark-default";
    public List<string> SearchFolders { get; set; } = DefaultSearchFolders();
    public List<string> AppDirectories { get; set; } = DefaultAppDirectories();

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
            var resolved = BrowserDiscovery.Resolve(Browser);
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
            var resolved = TerminalDiscovery.Resolve(Terminal);
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

    public static List<string> DefaultAppDirectories() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) {
            return [
                "/Applications",
                Path.Combine(home, "Applications"),
            ];
        }
        if (OperatingSystem.IsWindows()) {
            return [
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            ];
        }
        // Linux
        return [
            "/usr/share/applications",
            "/usr/local/share/applications",
            Path.Combine(home, ".local", "share", "applications"),
        ];
    }

    public static List<string> DefaultSearchFolders() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) {
            return [
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Desktop"),
                Path.Combine(home, "Documents"),
                Path.Combine(home, "Movies"),
                Path.Combine(home, "Pictures"),
            ];
        }
        if (OperatingSystem.IsWindows()) {
            return [
                Path.Combine(home, "Downloads"),
                Path.Combine(home, "Desktop"),
                Path.Combine(home, "Documents"),
                Path.Combine(home, "Videos"),
                Path.Combine(home, "Pictures"),
            ];
        }
        // Linux
        return [
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents"),
            Path.Combine(home, "Videos"),
            Path.Combine(home, "Pictures"),
        ];
    }

    public static UserSettings Load() {
        try {
            if (!File.Exists(SettingsPath)) return new UserSettings();
            var json = JsonNode.Parse(File.ReadAllText(SettingsPath));
            if (json == null) return new UserSettings();

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

            return new UserSettings {
                Browser        = json["browser"]?.GetValue<string>()  ?? "",
                Terminal       = json["terminal"]?.GetValue<string>() ?? "",
                Theme          = json["theme"]?.GetValue<string>()    ?? "dark-default",
                SearchFolders  = folders?.Count > 0 ? folders : DefaultSearchFolders(),
                AppDirectories = appDirs?.Count > 0 ? appDirs : DefaultAppDirectories(),
            };
        } catch {
            return new UserSettings();
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

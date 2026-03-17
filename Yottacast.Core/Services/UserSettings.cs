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

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yottacast", "settings.json");

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

            return new UserSettings {
                Browser       = json["browser"]?.GetValue<string>()  ?? "",
                Terminal      = json["terminal"]?.GetValue<string>() ?? "",
                Theme         = json["theme"]?.GetValue<string>()    ?? "dark-default",
                SearchFolders = folders?.Count > 0 ? folders : DefaultSearchFolders(),
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
            var json = new JsonObject {
                ["browser"]       = Browser,
                ["terminal"]      = Terminal,
                ["theme"]         = Theme,
                ["searchFolders"] = foldersArray,
            };
            File.WriteAllText(SettingsPath,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) {
            Console.WriteLine($"[Settings] Save error: {ex.Message}");
        }
    }
}

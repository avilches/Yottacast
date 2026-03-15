using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yottacast.Services;

public class UserSettings {
    public string Browser  { get; set; } = "";
    public string Terminal { get; set; } = "";
    public string Theme    { get; set; } = "dark-default";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yottacast", "settings.json");

    public static UserSettings Load() {
        try {
            if (!File.Exists(SettingsPath)) return new UserSettings();
            var json = JsonNode.Parse(File.ReadAllText(SettingsPath));
            if (json == null) return new UserSettings();
            return new UserSettings {
                Browser  = json["browser"]?.GetValue<string>()  ?? "",
                Terminal = json["terminal"]?.GetValue<string>() ?? "",
                Theme    = json["theme"]?.GetValue<string>()    ?? "dark-default",
            };
        } catch {
            return new UserSettings();
        }
    }

    public void Save() {
        try {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = new JsonObject {
                ["browser"]  = Browser,
                ["terminal"] = Terminal,
                ["theme"]    = Theme,
            };
            File.WriteAllText(SettingsPath,
                json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) {
            Console.WriteLine($"[Settings] Save error: {ex.Message}");
        }
    }
}

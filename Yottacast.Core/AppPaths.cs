namespace Yottacast.Core;

/// <summary>
/// Single source of truth for all runtime file/directory paths that Yottacast writes to disk.
/// Every path the app reads or writes at runtime is defined here.
/// </summary>
public static class AppPaths {
    // ── Base directories ─────────────────────────────────────────────────────

    /// <summary>Main config directory: ~/Library/Application Support/Yottacast (macOS).</summary>
    public static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yottacast");

    /// <summary>Log directory: ~/Library/Logs/Yottacast (macOS) or %LOCALAPPDATA%/Yottacast/Logs (Windows).</summary>
    public static readonly string LogDir = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Yottacast")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yottacast", "Logs");

    /// <summary>Cache directory: ~/.cache/yottacast (all platforms).</summary>
    public static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "yottacast");

    // ── Specific paths ───────────────────────────────────────────────────────

    /// <summary>User settings JSON file.</summary>
    public static readonly string SettingsFile = Path.Combine(ConfigDir, "settings.json");

    /// <summary>Emoji compact cache JSON file.</summary>
    public static readonly string EmojiCacheFile = Path.Combine(ConfigDir, "emoji-cache.json");

    /// <summary>Log file pattern (Serilog rolling daily).</summary>
    public static readonly string LogFilePattern = Path.Combine(LogDir, "yottacast-.log");

    /// <summary>App icon disk cache directory.</summary>
    public static readonly string AppIconCacheDir = Path.Combine(CacheDir, "app-icons");

    /// <summary>File icon disk cache directory.</summary>
    public static readonly string FileIconCacheDir = Path.Combine(CacheDir, "file-icons");

    /// <summary>Badge icon disk cache directory (default app icon per file extension).</summary>
    public static readonly string BadgeIconCacheDir = Path.Combine(CacheDir, "badge-icons");

    /// <summary>User-installed WebSearch plugin JSON files directory.</summary>
    public static readonly string PluginsDir = Path.Combine(ConfigDir, "plugins");

    /// <summary>Cached icons for WebSearch plugins.</summary>
    public static readonly string PluginIconCacheDir = Path.Combine(CacheDir, "plugin-icons");
}
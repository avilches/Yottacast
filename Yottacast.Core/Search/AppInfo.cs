using System.Runtime.InteropServices;

namespace Yottacast.Core.Search;

/// <summary>
/// Represents an installed application with a lazily-resolved icon path.
/// </summary>
public sealed class AppInfo {
    public string Name { get; }
    public string Path { get; }

    // Read Info.plist on first access — avoids parsing hundreds of files at startup
    private readonly Lazy<string?> _iconPath;
    public string? IconPath => _iconPath.Value;

    internal AppInfo(string name, string path) {
        Name = name;
        Path = path;
        _iconPath = new Lazy<string?>(
            () => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? TryGetMacIconPath(path) : null,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static string? TryGetMacIconPath(string appPath) {
        try {
            var plist = System.IO.Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(plist)) return null;

            var content = File.ReadAllText(plist);
            var keyIdx = content.IndexOf("<key>CFBundleIconFile</key>", StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            var stringStart = content.IndexOf("<string>", keyIdx, StringComparison.Ordinal);
            if (stringStart < 0) return null;
            var stringEnd = content.IndexOf("</string>", stringStart + 8, StringComparison.Ordinal);
            if (stringEnd < 0) return null;

            var iconFile = content[(stringStart + 8)..stringEnd].Trim();
            if (!iconFile.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
                iconFile += ".icns";

            var iconPath = System.IO.Path.Combine(appPath, "Contents", "Resources", iconFile);
            return File.Exists(iconPath) ? iconPath : null;
        } catch {
            return null;
        }
    }
}
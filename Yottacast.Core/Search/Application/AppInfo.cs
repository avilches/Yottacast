namespace Yottacast.Core.Search.Application;

/// <summary>
/// Represents an installed application with a lazily-resolved icon path.
/// </summary>
public sealed class AppInfo {
    public string Name { get; }
    public string Path { get; }

    // Resolved on first access — avoids expensive I/O at startup
    private readonly Lazy<string?> _iconPath;
    public string? IconPath => _iconPath.Value;

    internal AppInfo(string name, string path, Func<string, string?> getIconPath) {
        Name = name;
        Path = path;
        _iconPath = new Lazy<string?>(
            () => getIconPath(path),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }
}

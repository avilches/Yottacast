using Yottacast.Core.Search.UserDocuments;

namespace Yottacast.Core.Platform;

public abstract class PlatformProvider {
    public abstract bool? IsSystemDarkMode();

    public virtual string DefaultTheme() => IsSystemDarkMode() switch {
        true  => "dark-default",
        false => "light-gray",
        null  => "dark-default",
    };

    public abstract List<string> DefaultAppDirectories();
    public abstract List<string> DefaultSearchFolders();

    public abstract Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct);

    public abstract IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved);

    public abstract void LaunchApp(string path);

    public abstract Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct);

    public abstract string[] KnownBrowserNames { get; }
    public abstract IReadOnlyDictionary<string, string[]> BrowserFallbackPaths { get; }
    public abstract void OpenUrl(string url, string browserName);
    public abstract string[] GetBrowserPaths(string name);

    public abstract string[] KnownTerminalNames { get; }
    public abstract IReadOnlyDictionary<string, string[]> TerminalFallbackPaths { get; }
    public abstract void ExecuteCommand(string command, string terminalName);
    public abstract string[] GetTerminalPaths(string name);

    public abstract string? GetAppIconPath(string appPath);

    public static string ExpandPath(string path) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "$HOME" || path == "~")
            return home;
        if (path.StartsWith("$HOME/", StringComparison.Ordinal))
            return Path.Combine(home, path[6..]);
        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(home, path[2..]);
        return path;
    }
}

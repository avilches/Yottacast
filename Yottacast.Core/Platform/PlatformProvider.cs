using Yottacast.Core.Search.UserDocuments;

namespace Yottacast.Core.Platform;

public record RunningAppInfo(string Path, int Pid);

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

    /// <summary>
    /// Returns the expected path of an app with the given name inside a directory,
    /// or null if the platform doesn't support directory-based app lookup (e.g. Windows).
    /// </summary>
    public virtual string? AppPathInDirectory(string dir, string appName) => null;

    public abstract string[] KnownBrowserNames { get; }
    public virtual IReadOnlyDictionary<string, string[]> BrowserKnownPaths =>
        new Dictionary<string, string[]>();
    public abstract void OpenUrl(string url, string browserName);
    public abstract string[] KnownTerminalNames { get; }
    public virtual IReadOnlyDictionary<string, string[]> TerminalKnownPaths =>
        new Dictionary<string, string[]>();
    public abstract void ExecuteCommand(string command, string terminalName);

    /// <summary>Opens the given directory in the system file manager (Finder / Explorer).</summary>
    public virtual void RevealInFileManager(string directoryPath) { }

    /// <summary>Opens the given file with the system default application.</summary>
    public virtual void OpenFile(string filePath) { }

    public virtual byte[]? GetAppIconBytes(string appPath) => null;
    public virtual byte[]? GetFileIconBytes(string filePath) => null;
    public virtual string? GetDefaultAppPath(string filePath) => null;
    public virtual bool AreIconsSame(string path1, string path2) => false;

    /// <summary>Returns the name of the currently connected Wi-Fi network, or null if not connected.</summary>
    public virtual string? GetCurrentWifiNetworkName() => null;

    /// <summary>Returns the names of currently active (Connected) VPN connections.</summary>
    public virtual IReadOnlyList<string> GetActiveVpnNames() => [];

    /// <summary>Returns the list of currently running applications with their process IDs.</summary>
    public virtual IReadOnlyList<RunningAppInfo> GetRunningApps() => [];

    /// <summary>Requests the application with the given PID to quit gracefully (SIGTERM / CloseMainWindow).</summary>
    public virtual void QuitApp(int pid) { }

    /// <summary>Forcefully terminates the application with the given PID (SIGKILL / Kill).</summary>
    public virtual void ForceQuitApp(int pid) { }

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

    public static string CollapseHomePath(string path) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.Equals(path, home, StringComparison.OrdinalIgnoreCase))
            return "$HOME";
        var prefix = home.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return "$HOME/" + path[prefix.Length..];
        return path;
    }
}

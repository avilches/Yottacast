using System.Threading;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;

namespace Yottacast.Core.Tests;

/// <summary>Minimal PlatformProvider stub for use across test classes.</summary>
internal sealed class MinimalPlatform : PlatformProvider {
    private readonly string _defaultTheme;
    private readonly string _searchFolderDefault;

    /// <summary>
    /// The search folder passed to the constructor must be a path that exists on disk,
    /// because UserSettings filters defaults to existing directories on first launch.
    /// </summary>
    public MinimalPlatform(string defaultTheme = "dark-default", string? searchFolderDefault = null) {
        _defaultTheme = defaultTheme;
        _searchFolderDefault = searchFolderDefault
            ?? $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/Documents";
    }

    public override bool? IsSystemDarkMode() => null;
    public override string DefaultTheme() => _defaultTheme;
    public override List<string> DefaultAppDirectories() => ["/apps"];
    public override List<string> DefaultSearchFolders() => [_searchFolderDefault];

    public override Task ScanAppsAsync(Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) => Task.CompletedTask;
    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];
    public override void LaunchApp(string path) { }
    public override Task SearchFilesAsync(string query, Action<FileResult> onResult, int maxResults, IReadOnlyList<string>? folders, CancellationToken ct) => Task.CompletedTask;

    public override string[] KnownBrowserNames => [];
    public override void OpenUrl(string url, string browserName) { }

    public override string[] KnownTerminalNames => [];
    public override void ExecuteCommand(string command, string terminalName) { }
}

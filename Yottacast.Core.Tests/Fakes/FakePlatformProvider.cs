using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Fakes;

/// <summary>
/// Minimal PlatformProvider for tests. SearchFilesAsync emits the provided FileResults
/// regardless of query or folders — callers control exactly what "the platform found".
/// Members that subclasses may need to override are declared virtual.
/// </summary>
internal class FakePlatformProvider(IReadOnlyList<FileResult> files) : PlatformProvider {
    public override bool? IsSystemDarkMode() => null;
    public override List<string> DefaultAppDirectories() => [];
    public override List<string> DefaultSearchFolders() => [];

    public override Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct) {
        foreach (var f in files)
            onResult(f);
        return Task.CompletedTask;
    }

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) =>
        Task.CompletedTask;

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];

    public override void LaunchApp(string path) { }

    public override string[] KnownBrowserNames => [];
    public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths =>
        new Dictionary<string, string[]>();
    public override void OpenUrl(string url, string browserName) { }
    public override string[] GetBrowserPaths(string name) => [];

    public override string[] KnownTerminalNames => [];
    public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths =>
        new Dictionary<string, string[]>();
    public override void ExecuteCommand(string command, string terminalName) { }
    public override string[] GetTerminalPaths(string name) => [];

}

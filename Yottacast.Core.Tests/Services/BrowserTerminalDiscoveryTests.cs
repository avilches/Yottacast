using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Services;

/// <summary>
/// Tests for BrowserDiscovery and TerminalDiscovery.
///
/// Discovery checks Directory.Exists || File.Exists on each candidate path,
/// so tests create real temp files/directories on disk.
///
/// Search order: user app directories → platform default directories → platform known paths.
/// Directories are deduplicated to avoid checking the same path twice.
/// </summary>
public class BrowserTerminalDiscoveryTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastTests_{Guid.NewGuid():N}");

    public BrowserTerminalDiscoveryTests() {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private string TempFile(string name) {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "");
        return path;
    }

    private string TempDir(string name) {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string NonExistentPath(string name) =>
        Path.Combine(_tempDir, name);

    private static UserSettings BuildSettings(FakePlatformProvider platform) =>
        UserSettings.Load(platform);

    // ─── BrowserDiscovery.Resolve — preferred name found in app directory ────

    [Fact]
    public void Resolve_Browser_PreferredNameFound_ReturnsPreferredBrowser() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Chrome.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome", "Safari"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Chrome", result.Name);
        Assert.Contains("Chrome.app", result.ExecutablePath);
    }

    [Fact]
    public void Resolve_Browser_PreferredNameIsDirectory_ReturnsPreferredBrowser() {
        var appDir = TempDir("apps");
        var safariBundle = TempDir(Path.Combine("apps", "Safari.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Safari"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = BrowserDiscovery.Resolve("Safari", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Safari", result.Name);
        Assert.Equal(safariBundle, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — empty preferred name falls back ───────────

    [Fact]
    public void Resolve_Browser_EmptyPreferredName_ReturnsFallback() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Firefox.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome", "Firefox"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = BrowserDiscovery.Resolve("", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Firefox", result.Name);
    }

    // ─── BrowserDiscovery.Resolve — saved name not on disk, falls back ────────

    [Fact]
    public void Resolve_Browser_SavedNameNotOnDisk_FallsBackToFirstAvailable() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Edge.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome", "Edge"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Edge", result.Name);
    }

    // ─── BrowserDiscovery.Resolve — no browser found at all ──────────────────

    [Fact]
    public void Resolve_Browser_NoBrowserInstalled_ReturnsNull() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome", "Firefox"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform, []);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_Browser_NoKnownBrowsers_ReturnsNull() {
        var platform = new BrowserTerminalFakePlatform(browsers: [], terminals: []);

        var result = BrowserDiscovery.Resolve("", platform, []);

        Assert.Null(result);
    }

    // ─── BrowserDiscovery.Resolve — known paths (Windows-style) ──────────────

    [Fact]
    public void Resolve_Browser_FoundViaKnownPaths() {
        var chromePath = TempFile("chrome.exe");

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome"],
            terminals: [],
            browserKnownPaths: new() { ["Chrome"] = [chromePath] }
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform, []);

        Assert.NotNull(result);
        Assert.Equal("Chrome", result.Name);
        Assert.Equal(chromePath, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — user dir takes priority over known paths ─

    [Fact]
    public void Resolve_Browser_UserDirPriorityOverKnownPaths() {
        var appDir = TempDir("apps");
        var userChrome = TempDir(Path.Combine("apps", "Chrome.app"));
        var knownChrome = TempFile("chrome-known.exe");

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app",
            browserKnownPaths: new() { ["Chrome"] = [knownChrome] }
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal(userChrome, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — default dirs used as fallback ─────────────

    [Fact]
    public void Resolve_Browser_FoundInDefaultDirs() {
        var defaultDir = TempDir("default-apps");
        TempDir(Path.Combine("default-apps", "Safari.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Safari"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app",
            defaultAppDirs: [defaultDir]
        );

        // No user dirs — should find in default dirs
        var result = BrowserDiscovery.Resolve("Safari", platform, []);

        Assert.NotNull(result);
        Assert.Equal("Safari", result.Name);
    }

    // ─── BrowserDiscovery.Resolve — deduplication across user and default dirs

    [Fact]
    public void Resolve_Browser_DeduplicatesDirs() {
        var sharedDir = TempDir("shared-apps");
        TempDir(Path.Combine("shared-apps", "Chrome.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app",
            defaultAppDirs: [sharedDir]  // same as user dir
        );

        // Both user dirs and default dirs point to the same place
        var result = BrowserDiscovery.Resolve("Chrome", platform, [sharedDir]);

        Assert.NotNull(result);
        Assert.Equal("Chrome", result.Name);
    }

    // ─── BrowserDiscovery.Discover — returns installed browsers ──────────────

    [Fact]
    public void Discover_Browser_ReturnsOnlyInstalled() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Safari.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Safari", "Chrome", "Firefox"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Safari", result[0].Name);
    }

    [Fact]
    public void Discover_Browser_NoneInstalled_ReturnsEmpty() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Chrome"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        var discovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);

        var result = discovery.Discover();

        Assert.Empty(result);
    }

    // ─── BrowserDiscovery cache ──────────────────────────────────────────────

    [Fact]
    public void Discover_Browser_CachesResults() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Safari.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Safari"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);

        var first = discovery.Discover();
        var second = discovery.Discover();

        Assert.Same(first, second);
    }

    [Fact]
    public void Discover_Browser_InvalidateCacheForcesRescan() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Safari.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: ["Safari"],
            terminals: [],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);

        var first = discovery.Discover();
        discovery.InvalidateCache();
        var second = discovery.Discover();

        Assert.NotSame(first, second);
        Assert.Equal(first.Count, second.Count);
    }

    // ─── TerminalDiscovery.Resolve — preferred name found ───────────────────

    [Fact]
    public void Resolve_Terminal_PreferredNameFound_ReturnsPreferredTerminal() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "iTerm.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["iTerm"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = TerminalDiscovery.Resolve("iTerm", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("iTerm", result.Name);
    }

    // ─── TerminalDiscovery.Resolve — empty preferred name falls back ─────────

    [Fact]
    public void Resolve_Terminal_EmptyPreferredName_ReturnsFallback() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Warp.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Terminal", "Warp"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = TerminalDiscovery.Resolve("", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Warp", result.Name);
    }

    // ─── TerminalDiscovery.Resolve — saved name not on disk, falls back ──────

    [Fact]
    public void Resolve_Terminal_SavedNameNotOnDisk_FallsBackToFirstAvailable() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Terminal.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp", "Terminal"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = TerminalDiscovery.Resolve("Warp", platform, [appDir]);

        Assert.NotNull(result);
        Assert.Equal("Terminal", result.Name);
    }

    // ─── TerminalDiscovery.Resolve — no terminal found at all ───────────────

    [Fact]
    public void Resolve_Terminal_NoTerminalInstalled_ReturnsNull() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Terminal", "iTerm"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );

        var result = TerminalDiscovery.Resolve("Terminal", platform, []);

        Assert.Null(result);
    }

    // ─── TerminalDiscovery.Resolve — known paths with wildcards skipped ──────

    [Fact]
    public void Resolve_Terminal_KnownPathWithWildcard_IsSkipped() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp"],
            terminalKnownPaths: new() { ["Warp"] = ["/Applications/Warp*.app"] }
        );

        var result = TerminalDiscovery.Resolve("Warp", platform, []);

        Assert.Null(result);
    }

    // ─── TerminalDiscovery.Resolve — found via known paths ──────────────────

    [Fact]
    public void Resolve_Terminal_FoundViaKnownPaths() {
        var termPath = TempFile("cmd.exe");

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Command Prompt"],
            terminalKnownPaths: new() { ["Command Prompt"] = [termPath] }
        );

        var result = TerminalDiscovery.Resolve("Command Prompt", platform, []);

        Assert.NotNull(result);
        Assert.Equal("Command Prompt", result.Name);
        Assert.Equal(termPath, result.ExecutablePath);
    }

    // ─── TerminalDiscovery.Discover — returns installed terminals ────────────

    [Fact]
    public void Discover_Terminal_ReturnsOnlyInstalled() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Warp.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp", "Terminal", "iTerm"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new TerminalDiscovery(settings, platform, NullLogger<TerminalDiscovery>.Instance);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Warp", result[0].Name);
    }

    [Fact]
    public void Discover_Terminal_NoneInstalled_ReturnsEmpty() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        var discovery = new TerminalDiscovery(settings, platform, NullLogger<TerminalDiscovery>.Instance);

        var result = discovery.Discover();

        Assert.Empty(result);
    }

    // ─── TerminalDiscovery cache ─────────────────────────────────────────────

    [Fact]
    public void Discover_Terminal_CachesResults() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Warp.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new TerminalDiscovery(settings, platform, NullLogger<TerminalDiscovery>.Instance);

        var first = discovery.Discover();
        var second = discovery.Discover();

        Assert.Same(first, second);
    }

    [Fact]
    public void Discover_Terminal_InvalidateCacheForcesRescan() {
        var appDir = TempDir("apps");
        TempDir(Path.Combine("apps", "Warp.app"));

        var platform = new BrowserTerminalFakePlatform(
            browsers: [],
            terminals: ["Warp"],
            appPathInDir: (dir, name) => $"{dir}/{name}.app"
        );
        var settings = BuildSettings(platform);
        settings.AppDirectories = [appDir];
        var discovery = new TerminalDiscovery(settings, platform, NullLogger<TerminalDiscovery>.Instance);

        var first = discovery.Discover();
        discovery.InvalidateCache();
        var second = discovery.Discover();

        Assert.NotSame(first, second);
        Assert.Equal(first.Count, second.Count);
    }
}

// ─── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>
/// FakePlatformProvider that supports controllable browser/terminal discovery.
/// Browsers and terminals are discovered via AppPathInDirectory (from directories)
/// and BrowserKnownPaths/TerminalKnownPaths (for Windows-style direct paths).
/// </summary>
internal sealed class BrowserTerminalFakePlatform : FakePlatformProvider {
    private readonly string[] _browsers;
    private readonly string[] _terminals;
    private readonly Dictionary<string, string[]>? _browserKnownPaths;
    private readonly Dictionary<string, string[]>? _terminalKnownPaths;
    private readonly Func<string, string, string?>? _appPathInDir;
    private readonly List<string>? _defaultAppDirs;

    public BrowserTerminalFakePlatform(
        string[] browsers,
        string[] terminals,
        Dictionary<string, string[]>? browserKnownPaths = null,
        Dictionary<string, string[]>? terminalKnownPaths = null,
        Func<string, string, string?>? appPathInDir = null,
        List<string>? defaultAppDirs = null)
        : base([]) {
        _browsers = browsers;
        _terminals = terminals;
        _browserKnownPaths = browserKnownPaths;
        _terminalKnownPaths = terminalKnownPaths;
        _appPathInDir = appPathInDir;
        _defaultAppDirs = defaultAppDirs;
    }

    public override string[] KnownBrowserNames  => _browsers;
    public override string[] KnownTerminalNames => _terminals;

    public override IReadOnlyDictionary<string, string[]> BrowserKnownPaths =>
        _browserKnownPaths ?? new Dictionary<string, string[]>();
    public override IReadOnlyDictionary<string, string[]> TerminalKnownPaths =>
        _terminalKnownPaths ?? new Dictionary<string, string[]>();

    public override List<string> DefaultAppDirectories() => _defaultAppDirs ?? [];

    public override string? AppPathInDirectory(string dir, string appName) =>
        _appPathInDir?.Invoke(dir, appName);
}

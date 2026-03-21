using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Services;

/// <summary>
/// Tests for BrowserDiscovery and TerminalDiscovery.
///
/// Resolve() checks Directory.Exists || File.Exists on each candidate path,
/// so tests that exercise Resolve() create real temp files/directories on disk.
/// Discover() fallback paths check File.Exists only.
/// </summary>
public class BrowserTerminalDiscoveryTests : IDisposable {
    // Temp directory used as a root for all fake app paths in this test class.
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastTests_{Guid.NewGuid():N}");

    public BrowserTerminalDiscoveryTests() {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a file under _tempDir and returns its full path.</summary>
    private string TempFile(string name) {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "");
        return path;
    }

    /// <summary>Creates a directory under _tempDir and returns its full path.</summary>
    private string TempDir(string name) {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Returns a path under _tempDir that does NOT exist on disk.</summary>
    private string NonExistentPath(string name) =>
        Path.Combine(_tempDir, name);

    /// <summary>
    /// Builds an ApplicationSearch with specific apps pre-loaded into its cache.
    /// apps is a list of (name, path) pairs that will be registered as if scanned.
    /// </summary>
    private ApplicationSearch BuildAppSearchWithApps(params (string name, string path)[] apps) {
        // Use a FakePlatformProvider whose ScanAppsAsync calls addApp for each app.
        var fakePlatform = new ScanningFakePlatform(apps);
        var settings = UserSettings.Load(fakePlatform);
        var appSearch = new ApplicationSearch(settings, fakePlatform, NullLogger<ApplicationSearch>.Instance);
        appSearch.Start();
        // Wait for the (synchronous fake) scan to complete.
        appSearch.WhenReady().GetAwaiter().GetResult();
        return appSearch;
    }

    /// <summary>Builds an ApplicationSearch with an empty cache.</summary>
    private ApplicationSearch BuildEmptyAppSearch() => BuildAppSearchWithApps();

    // ─── BrowserDiscovery.Resolve — preferred name found on disk ─────────────

    [Fact]
    public void Resolve_Browser_PreferredNameFound_ReturnsPreferredBrowser() {
        var chromePath = TempFile("Chrome.app");
        var safariPath = TempFile("Safari.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"]  = [chromePath],
                ["Safari"]  = [safariPath],
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform);

        Assert.NotNull(result);
        Assert.Equal("Chrome", result.Name);
        Assert.Equal(chromePath, result.ExecutablePath);
    }

    [Fact]
    public void Resolve_Browser_PreferredNameIsDirectory_ReturnsPreferredBrowser() {
        // macOS .app bundles are directories — Resolve accepts Directory.Exists too.
        var safariBundle = TempDir("Safari.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Safari"] = [safariBundle],
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("Safari", platform);

        Assert.NotNull(result);
        Assert.Equal("Safari", result.Name);
        Assert.Equal(safariBundle, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — empty preferred name falls back ───────────

    [Fact]
    public void Resolve_Browser_EmptyPreferredName_ReturnsFallback() {
        var firefoxPath = TempFile("Firefox.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"]  = [NonExistentPath("Chrome.app")],
                ["Firefox"] = [firefoxPath],
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("", platform);

        Assert.NotNull(result);
        Assert.Equal("Firefox", result.Name);
        Assert.Equal(firefoxPath, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — saved name not on disk, falls back ────────

    [Fact]
    public void Resolve_Browser_SavedNameNotOnDisk_FallsBackToFirstAvailable() {
        var edgePath = TempFile("Edge.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"] = [NonExistentPath("Chrome.app")],   // not installed
                ["Edge"]   = [edgePath],                         // installed
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform);

        Assert.NotNull(result);
        Assert.Equal("Edge", result.Name);
        Assert.Equal(edgePath, result.ExecutablePath);
    }

    // ─── BrowserDiscovery.Resolve — no browser found at all ──────────────────

    [Fact]
    public void Resolve_Browser_NoBrowserInstalled_ReturnsNull() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"]  = [NonExistentPath("Chrome.app")],
                ["Firefox"] = [NonExistentPath("Firefox.app")],
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_Browser_NoKnownBrowsers_ReturnsNull() {
        // KnownBrowserNames empty → nothing to iterate.
        var platform = new BrowserTerminalFakePlatform(browsers: new(), terminals: new());

        var result = BrowserDiscovery.Resolve("", platform);

        Assert.Null(result);
    }

    // ─── BrowserDiscovery.Resolve — first path wins among candidates ──────────

    [Fact]
    public void Resolve_Browser_MultiplePathsForName_ReturnsFirstExisting() {
        var secondPath = TempFile("Chrome_v2.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"] = [NonExistentPath("Chrome_v1.app"), secondPath],
            },
            terminals: new()
        );

        var result = BrowserDiscovery.Resolve("Chrome", platform);

        Assert.NotNull(result);
        Assert.Equal(secondPath, result.ExecutablePath);
    }

    // ─── TerminalDiscovery.Resolve — preferred name found on disk ────────────

    [Fact]
    public void Resolve_Terminal_PreferredNameFound_ReturnsPreferredTerminal() {
        var iTermPath = TempDir("iTerm.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() {
                ["iTerm"] = [iTermPath],
            }
        );

        var result = TerminalDiscovery.Resolve("iTerm", platform);

        Assert.NotNull(result);
        Assert.Equal("iTerm", result.Name);
        Assert.Equal(iTermPath, result.ExecutablePath);
    }

    // ─── TerminalDiscovery.Resolve — empty preferred name falls back ──────────

    [Fact]
    public void Resolve_Terminal_EmptyPreferredName_ReturnsFallback() {
        var warpPath = TempDir("Warp.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() {
                ["Terminal"]  = [NonExistentPath("Terminal.app")],
                ["Warp"]      = [warpPath],
            }
        );

        var result = TerminalDiscovery.Resolve("", platform);

        Assert.NotNull(result);
        Assert.Equal("Warp", result.Name);
    }

    // ─── TerminalDiscovery.Resolve — saved name not on disk, falls back ───────

    [Fact]
    public void Resolve_Terminal_SavedNameNotOnDisk_FallsBackToFirstAvailable() {
        var terminalPath = TempFile("Terminal.app");

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() {
                ["Warp"]     = [NonExistentPath("Warp.app")],
                ["Terminal"] = [terminalPath],
            }
        );

        var result = TerminalDiscovery.Resolve("Warp", platform);

        Assert.NotNull(result);
        Assert.Equal("Terminal", result.Name);
    }

    // ─── TerminalDiscovery.Resolve — no terminal found at all ────────────────

    [Fact]
    public void Resolve_Terminal_NoTerminalInstalled_ReturnsNull() {
        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() {
                ["Terminal"] = [NonExistentPath("Terminal.app")],
                ["iTerm"]    = [NonExistentPath("iTerm.app")],
            }
        );

        var result = TerminalDiscovery.Resolve("Terminal", platform);

        Assert.Null(result);
    }

    // ─── BrowserDiscovery.Discover — uses ApplicationSearch cache ────────────

    [Fact]
    public void Discover_Browser_UsesAppSearchCache_WhenCachePopulated() {
        var safariPath = TempDir("Safari.app");
        var appSearch = BuildAppSearchWithApps(("Safari", safariPath));

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() { ["Safari"] = [safariPath] },
            terminals: new()
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Safari", result[0].Name);
        Assert.Equal(safariPath, result[0].ExecutablePath);
    }

    [Fact]
    public void Discover_Browser_CacheEmpty_FallsBackToFallbackPaths() {
        var chromePath = TempFile("google-chrome");
        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() { ["Chrome"] = [] },
            terminals: new(),
            browserFallbackPaths: new() { ["Chrome"] = [chromePath] }
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Chrome", result[0].Name);
        Assert.Equal(chromePath, result[0].ExecutablePath);
    }

    [Fact]
    public void Discover_Browser_FallbackPathNotOnDisk_Excluded() {
        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() { ["Chrome"] = [] },
            terminals: new(),
            browserFallbackPaths: new() { ["Chrome"] = [NonExistentPath("chrome")] }
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_Browser_MultipleKnownBrowsers_ReturnsOnlyInstalled() {
        var safariPath = TempDir("Safari.app");
        var appSearch = BuildAppSearchWithApps(("Safari", safariPath));

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Safari"]  = [safariPath],
                ["Chrome"]  = [NonExistentPath("Chrome.app")],
                ["Firefox"] = [],
            },
            terminals: new(),
            browserFallbackPaths: new() {
                ["Chrome"]  = [NonExistentPath("chrome")],
                ["Firefox"] = [NonExistentPath("firefox")],
            }
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Safari", result[0].Name);
    }

    // ─── TerminalDiscovery.Discover — uses ApplicationSearch cache ────────────

    [Fact]
    public void Discover_Terminal_UsesAppSearchCache_WhenCachePopulated() {
        var iTermPath = TempDir("iTerm.app");
        var appSearch = BuildAppSearchWithApps(("iTerm", iTermPath));

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() { ["iTerm"] = [iTermPath] }
        );
        var discovery = new TerminalDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("iTerm", result[0].Name);
        Assert.Equal(iTermPath, result[0].ExecutablePath);
    }

    [Fact]
    public void Discover_Terminal_CacheEmpty_FallsBackToFallbackPaths() {
        var terminalBinary = TempFile("my-terminal");

        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() { ["MyTerminal"] = [] },
            terminalFallbackPaths: new() { ["MyTerminal"] = [terminalBinary] }
        );
        var discovery = new TerminalDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("MyTerminal", result[0].Name);
        Assert.Equal(terminalBinary, result[0].ExecutablePath);
    }

    [Fact]
    public void Discover_Terminal_FallbackPathNotOnDisk_Excluded() {
        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() { ["Warp"] = [] },
            terminalFallbackPaths: new() { ["Warp"] = [NonExistentPath("Warp.app")] }
        );
        var discovery = new TerminalDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_Terminal_FallbackPathWithWildcard_IsSkipped() {
        // TerminalDiscovery.Discover skips paths that contain '*'.
        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() { ["Warp"] = [] },
            terminalFallbackPaths: new() { ["Warp"] = ["/Applications/Warp*.app"] }
        );
        var discovery = new TerminalDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_Terminal_MultipleKnownTerminals_ReturnsOnlyInstalled() {
        var warpPath = TempDir("Warp.app");
        var appSearch = BuildAppSearchWithApps(("Warp", warpPath));

        var platform = new BrowserTerminalFakePlatform(
            browsers: new(),
            terminals: new() {
                ["Warp"]     = [warpPath],
                ["Terminal"] = [NonExistentPath("Terminal.app")],
                ["iTerm"]    = [],
            },
            terminalFallbackPaths: new() {
                ["Terminal"] = [NonExistentPath("Terminal.app")],
                ["iTerm"]    = [NonExistentPath("iTerm.app")],
            }
        );
        var discovery = new TerminalDiscovery(appSearch, platform);

        var result = discovery.Discover();

        Assert.Single(result);
        Assert.Equal("Warp", result[0].Name);
    }

    // ─── BrowserDiscovery.GetCandidatePaths ───────────────────────────────────

    [Fact]
    public void GetCandidatePaths_Browser_PrefersAppSearchCache_OverGetBrowserPaths() {
        var cachedPath = TempDir("Safari.app");
        var alternativePath = TempDir("Safari_alt.app");
        var appSearch = BuildAppSearchWithApps(("Safari", cachedPath));

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() { ["Safari"] = [alternativePath] },
            terminals: new()
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var candidates = discovery.GetCandidatePaths();

        Assert.Single(candidates);
        Assert.Equal("Safari", candidates[0].Name);
        Assert.Equal(cachedPath, candidates[0].Path);
    }

    [Fact]
    public void GetCandidatePaths_Browser_FallsBackToGetBrowserPaths_WhenNotInCache() {
        var chromePath = TempFile("Chrome.exe");
        var appSearch = BuildEmptyAppSearch();

        var platform = new BrowserTerminalFakePlatform(
            browsers: new() { ["Chrome"] = [chromePath] },
            terminals: new()
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var candidates = discovery.GetCandidatePaths();

        Assert.Single(candidates);
        Assert.Equal("Chrome", candidates[0].Name);
        Assert.Equal(chromePath, candidates[0].Path);
    }

    [Fact]
    public void GetCandidatePaths_Browser_ExcludesNamesWithNoPath() {
        var appSearch = BuildEmptyAppSearch();

        // GetBrowserPaths returns empty array → no path → excluded
        var platform = new BrowserTerminalFakePlatform(
            browsers: new() {
                ["Chrome"]  = [],
                ["Firefox"] = [],
            },
            terminals: new()
        );
        var discovery = new BrowserDiscovery(appSearch, platform);

        var candidates = discovery.GetCandidatePaths();

        Assert.Empty(candidates);
    }
}

// ─── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>
/// FakePlatformProvider that supports controllable browser/terminal path mappings.
/// KnownBrowserNames / KnownTerminalNames are derived from the keys of the provided dictionaries,
/// preserving insertion order.
/// </summary>
internal sealed class BrowserTerminalFakePlatform(
    Dictionary<string, string[]> browsers,
    Dictionary<string, string[]> terminals,
    Dictionary<string, string[]>? browserFallbackPaths = null,
    Dictionary<string, string[]>? terminalFallbackPaths = null)
    : FakePlatformProvider([]) {
    private readonly Dictionary<string, string[]> _browserFallback = browserFallbackPaths  ?? new();
    private readonly Dictionary<string, string[]> _terminalFallback = terminalFallbackPaths ?? new();

    public override string[] KnownBrowserNames  => [.. browsers.Keys];
    public override string[] KnownTerminalNames => [.. terminals.Keys];

    public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths  => _browserFallback;
    public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths => _terminalFallback;

    public override string[] GetBrowserPaths(string name)  =>
        browsers.TryGetValue(name, out var p)  ? p : [];

    public override string[] GetTerminalPaths(string name) =>
        terminals.TryGetValue(name, out var p) ? p : [];
}

/// <summary>
/// FakePlatformProvider whose ScanAppsAsync calls addApp for each pre-configured app path.
/// Used to pre-populate ApplicationSearch cache without real OS calls.
/// </summary>
internal sealed class ScanningFakePlatform : FakePlatformProvider {
    private readonly (string name, string path)[] _apps;

    public ScanningFakePlatform(params (string name, string path)[] apps) : base([]) {
        _apps = apps;
    }

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        // addApp expects the full path; ApplicationSearch derives the name via GetFileNameWithoutExtension.
        // We store apps as (name, path) — pass the path so the name is recovered correctly.
        foreach (var (_, path) in _apps)
            addApp(path);
        return Task.CompletedTask;
    }
}

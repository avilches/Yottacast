using System.Text.Json;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

/// <summary>
/// Tests for UserSettings. File I/O tests pass a temp file path directly to
/// UserSettings.Load(settingsPath:) so they never touch the real settings file.
/// Browser/Terminal auto-repair tests use PlatformWithApps, which returns real
/// temp file paths that BrowserDiscovery.Resolve / TerminalDiscovery.Resolve can
/// verify on disk with File.Exists / Directory.Exists.
/// </summary>
public class UserSettingsTests : IDisposable {
    // ── Temp dir plumbing ─────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _settingsFile;

    public UserSettingsTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsFile = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helper: load settings pointed at the temp file ────────────────────────

    private UserSettings Load(PlatformProvider? platform = null) =>
        UserSettings.Load(platform ?? new MinimalPlatform(), settingsPath: _settingsFile);

    // ── Minimal platform providers ────────────────────────────────────────────

    /// <summary>Platform provider with no browsers/terminals and a fixed default theme.</summary>
    private sealed class MinimalPlatform : PlatformProvider {
        private readonly string _defaultTheme;
        public MinimalPlatform(string defaultTheme = "dark-default") => _defaultTheme = defaultTheme;

        public override bool? IsSystemDarkMode() => null;
        public override string DefaultTheme() => _defaultTheme;
        public override List<string> DefaultAppDirectories() => ["/apps"];
        public override List<string> DefaultSearchFolders() =>
            [$"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/Documents"];

        public override Task ScanAppsAsync(Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) => Task.CompletedTask;
        public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];
        public override void LaunchApp(string path) { }
        public override Task SearchFilesAsync(string query, Action<FileResult> onResult, int maxResults, IReadOnlyList<string>? folders, CancellationToken ct) => Task.CompletedTask;

        public override string[] KnownBrowserNames => [];
        public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths => new Dictionary<string, string[]>();
        public override void OpenUrl(string url, string browserName) { }
        public override string[] GetBrowserPaths(string name) => [];

        public override string[] KnownTerminalNames => [];
        public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths => new Dictionary<string, string[]>();
        public override void ExecuteCommand(string command, string terminalName) { }
        public override string[] GetTerminalPaths(string name) => [];

        public override string? GetAppIconPath(string appPath) => null;
    }

    /// <summary>
    /// Platform provider where browser/terminal paths are backed by real temp files
    /// so BrowserDiscovery.Resolve / TerminalDiscovery.Resolve can find them on disk.
    /// </summary>
    private sealed class PlatformWithApps : PlatformProvider {
        private readonly Dictionary<string, string[]> _browserPaths;
        private readonly Dictionary<string, string[]> _terminalPaths;

        public PlatformWithApps(
            Dictionary<string, string[]> browserPaths,
            Dictionary<string, string[]> terminalPaths) {
            _browserPaths  = browserPaths;
            _terminalPaths = terminalPaths;
        }

        public override bool? IsSystemDarkMode() => null;
        public override string DefaultTheme() => "dark-default";
        public override List<string> DefaultAppDirectories() => [];
        public override List<string> DefaultSearchFolders() => [];

        public override Task ScanAppsAsync(Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) => Task.CompletedTask;
        public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];
        public override void LaunchApp(string path) { }
        public override Task SearchFilesAsync(string query, Action<FileResult> onResult, int maxResults, IReadOnlyList<string>? folders, CancellationToken ct) => Task.CompletedTask;

        public override string[] KnownBrowserNames => [.. _browserPaths.Keys];
        public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths => _browserPaths;
        public override void OpenUrl(string url, string browserName) { }
        public override string[] GetBrowserPaths(string name) =>
            _browserPaths.TryGetValue(name, out var p) ? p : [];

        public override string[] KnownTerminalNames => [.. _terminalPaths.Keys];
        public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths => _terminalPaths;
        public override void ExecuteCommand(string command, string terminalName) { }
        public override string[] GetTerminalPaths(string name) =>
            _terminalPaths.TryGetValue(name, out var p) ? p : [];

        public override string? GetAppIconPath(string appPath) => null;
    }

    /// <summary>Minimal platform whose only configurable behaviour is IsSystemDarkMode.</summary>
    private sealed class DarkModePlatform(bool? isDark) : PlatformProvider {
        public override bool? IsSystemDarkMode() => isDark;
        public override List<string> DefaultAppDirectories() => [];
        public override List<string> DefaultSearchFolders() => [];
        public override Task ScanAppsAsync(Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) => Task.CompletedTask;
        public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];
        public override void LaunchApp(string path) { }
        public override Task SearchFilesAsync(string query, Action<FileResult> onResult, int maxResults, IReadOnlyList<string>? folders, CancellationToken ct) => Task.CompletedTask;
        public override string[] KnownBrowserNames => [];
        public override IReadOnlyDictionary<string, string[]> BrowserFallbackPaths => new Dictionary<string, string[]>();
        public override void OpenUrl(string url, string browserName) { }
        public override string[] GetBrowserPaths(string name) => [];
        public override string[] KnownTerminalNames => [];
        public override IReadOnlyDictionary<string, string[]> TerminalFallbackPaths => new Dictionary<string, string[]>();
        public override void ExecuteCommand(string command, string terminalName) { }
        public override string[] GetTerminalPaths(string name) => [];
        public override string? GetAppIconPath(string appPath) => null;
    }

    // ── Helper: create a real temp file representing an installed app ──────────

    private string CreateTempApp(string name) {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "");
        return path;
    }

    // ── Helper: write a settings JSON to the temp file ────────────────────────

    private void WriteSettingsJson(string json) {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
        File.WriteAllText(_settingsFile, json);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Load() — missing file → creates defaults
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Load_WhenFileDoesNotExist_CreatesSettingsFileOnDisk() {
        Assert.False(File.Exists(_settingsFile));

        Load();

        Assert.True(File.Exists(_settingsFile), "Load should create the settings file");
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_UsesDefaultThemeFromPlatform() {
        var settings = Load(new MinimalPlatform("light-gray"));

        Assert.Equal("light-gray", settings.Theme);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_UsesDefaultSearchFoldersFromPlatform() {
        var platform = new MinimalPlatform();
        var settings = Load(platform);

        Assert.Equal(platform.DefaultSearchFolders(), settings.SearchFolders);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_UsesDefaultAppDirectoriesFromPlatform() {
        var platform = new MinimalPlatform();
        var settings = Load(platform);

        Assert.Equal(platform.DefaultAppDirectories(), settings.AppDirectories);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Load() — existing file → reads values correctly
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Load_ExistingFile_RestoresBrowserAndTerminal() {
        WriteSettingsJson("""
            {
                "browser": "Firefox",
                "terminal": "iTerm",
                "theme": "dark-raycast",
                "searchFolders": ["/home/user/docs"],
                "appDirectories": ["/usr/apps"]
            }
            """);

        var settings = Load();

        Assert.Equal("Firefox", settings.Browser);
        Assert.Equal("iTerm",   settings.Terminal);
    }

    [Fact]
    public void Load_ExistingFile_RestoresTheme() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "light-blue",
                "searchFolders": ["/docs"],
                "appDirectories": ["/apps"]
            }
            """);

        var settings = Load();

        Assert.Equal("light-blue", settings.Theme);
    }

    [Fact]
    public void Load_ExistingFile_RestoresSearchFolders() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "dark-default",
                "searchFolders": ["/folder1", "/folder2"],
                "appDirectories": []
            }
            """);

        var settings = Load();

        Assert.Equal(["/folder1", "/folder2"], settings.SearchFolders);
    }

    [Fact]
    public void Load_ExistingFile_RestoresAppDirectories() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "dark-default",
                "searchFolders": ["/docs"],
                "appDirectories": ["/myapps", "/otherapps"]
            }
            """);

        var settings = Load();

        Assert.Equal(["/myapps", "/otherapps"], settings.AppDirectories);
    }

    [Fact]
    public void Load_ExistingFile_EmptyTheme_FallsBackToPlatformDefaultTheme() {
        // When the JSON has an empty theme string, Load should call platform.DefaultTheme()
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "",
                "searchFolders": ["/docs"],
                "appDirectories": ["/apps"]
            }
            """);

        var settings = Load(new MinimalPlatform("light-gray"));

        Assert.Equal("light-gray", settings.Theme);
    }

    [Fact]
    public void Load_ExistingFile_EmptySearchFolders_FallsBackToPlatformDefaults() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "dark-default",
                "searchFolders": [],
                "appDirectories": ["/apps"]
            }
            """);
        var platform = new MinimalPlatform();

        var settings = Load(platform);

        Assert.Equal(platform.DefaultSearchFolders(), settings.SearchFolders);
    }

    [Fact]
    public void Load_ExistingFile_EmptyAppDirectories_FallsBackToPlatformDefaults() {
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "theme": "dark-default",
                "searchFolders": ["/docs"],
                "appDirectories": []
            }
            """);
        var platform = new MinimalPlatform();

        var settings = Load(platform);

        Assert.Equal(platform.DefaultAppDirectories(), settings.AppDirectories);
    }

    [Fact]
    public void Load_MalformedJson_CreatesDefaultsInstead() {
        WriteSettingsJson("{ this is not valid json }}}");
        var platform = new MinimalPlatform("dark-default");

        var settings = Load(platform);

        // Should fall back to platform defaults without throwing
        Assert.Equal("dark-default", settings.Theme);
        Assert.Equal(platform.DefaultSearchFolders(), settings.SearchFolders);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Save() — writes current state to disk
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Save_WritesAllFieldsToDisk() {
        var settings = Load();
        settings.Browser        = "Safari";
        settings.Terminal       = "Warp";
        settings.Theme          = "dark-raycast";
        settings.SearchFolders  = ["/docs", "/downloads"];
        settings.AppDirectories = ["/Applications"];

        settings.Save();

        var raw = File.ReadAllText(_settingsFile);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        Assert.Equal("Safari",        root.GetProperty("browser").GetString());
        Assert.Equal("Warp",          root.GetProperty("terminal").GetString());
        Assert.Equal("dark-raycast",  root.GetProperty("theme").GetString());
        Assert.Equal("/docs",         root.GetProperty("searchFolders")[0].GetString());
        Assert.Equal("/downloads",    root.GetProperty("searchFolders")[1].GetString());
        Assert.Equal("/Applications", root.GetProperty("appDirectories")[0].GetString());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAllFields() {
        var settings = Load();
        settings.Browser        = "Chrome";
        settings.Terminal       = "Terminal";
        settings.Theme          = "light-blue";
        settings.SearchFolders  = ["/home/user/music"];
        settings.AppDirectories = ["/home/user/bin"];
        settings.Save();

        var reloaded = Load();

        Assert.Equal("Chrome",             reloaded.Browser);
        Assert.Equal("Terminal",           reloaded.Terminal);
        Assert.Equal("light-blue",         reloaded.Theme);
        Assert.Equal(["/home/user/music"], reloaded.SearchFolders);
        Assert.Equal(["/home/user/bin"],   reloaded.AppDirectories);
    }

    [Fact]
    public void Load_AlwaysCallsSaveAfterLoading() {
        // After Load the file must exist (even if it didn't before)
        Assert.False(File.Exists(_settingsFile));

        Load();

        Assert.True(File.Exists(_settingsFile), "Load must persist (Save) the settings file");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ExpandedSearchFolders / ExpandedAppDirectories
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExpandedSearchFolders_ExpandsTildePrefix() {
        var settings = Load();
        settings.SearchFolders = ["~/Documents", "~/Downloads"];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var expanded = settings.ExpandedSearchFolders;

        Assert.Equal(Path.Combine(home, "Documents"), expanded[0]);
        Assert.Equal(Path.Combine(home, "Downloads"), expanded[1]);
    }

    [Fact]
    public void ExpandedSearchFolders_ExpandsDollarHomePrefix() {
        var settings = Load();
        settings.SearchFolders = ["$HOME/Music", "$HOME/Videos"];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var expanded = settings.ExpandedSearchFolders;

        Assert.Equal(Path.Combine(home, "Music"),  expanded[0]);
        Assert.Equal(Path.Combine(home, "Videos"), expanded[1]);
    }

    [Fact]
    public void ExpandedSearchFolders_LeavesAbsolutePathsUnchanged() {
        var settings = Load();
        settings.SearchFolders = ["/absolute/path"];

        var expanded = settings.ExpandedSearchFolders;

        Assert.Equal("/absolute/path", expanded[0]);
    }

    [Fact]
    public void ExpandedAppDirectories_ExpandsTildePrefix() {
        var settings = Load();
        settings.AppDirectories = ["~/Applications"];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var expanded = settings.ExpandedAppDirectories;

        Assert.Equal(Path.Combine(home, "Applications"), expanded[0]);
    }

    [Fact]
    public void ExpandedAppDirectories_ExpandsDollarHomePrefix() {
        var settings = Load();
        settings.AppDirectories = ["$HOME/apps"];
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var expanded = settings.ExpandedAppDirectories;

        Assert.Equal(Path.Combine(home, "apps"), expanded[0]);
    }

    [Fact]
    public void ExpandedAppDirectories_LeavesAbsolutePathsUnchanged() {
        var settings = Load();
        settings.AppDirectories = ["/usr/local/bin"];

        var expanded = settings.ExpandedAppDirectories;

        Assert.Equal("/usr/local/bin", expanded[0]);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PlatformProvider.ExpandPath() — static utility
    // ══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string> ExpandPathCases {
        get {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new TheoryData<string, string> {
                // Tilde prefix
                { "~/Documents",        Path.Combine(home, "Documents") },
                { "~/sub/path",         Path.Combine(home, "sub/path")  },
                { "~",                  home                            },
                // $HOME prefix
                { "$HOME/Documents",    Path.Combine(home, "Documents") },
                { "$HOME/sub/path",     Path.Combine(home, "sub/path")  },
                { "$HOME",              home                            },
                // Paths that must pass through unchanged
                { "/absolute/path",     "/absolute/path"                },
                { "relative/path",      "relative/path"                 },
                { "",                   ""                              },
            };
        }
    }

    [Theory]
    [MemberData(nameof(ExpandPathCases))]
    public void ExpandPath_VariousInputs(string input, string expected) {
        Assert.Equal(expected, PlatformProvider.ExpandPath(input));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ActiveBrowser auto-repair
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ActiveBrowser_WhenBrowserIsEmpty_ReturnFirstAvailableBrowser() {
        var browserPath = CreateTempApp("FakeBrowser.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]> { ["FakeBrowser"] = [browserPath] },
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Browser = "";

        var result = settings.ActiveBrowser;

        Assert.NotNull(result);
        Assert.Equal("FakeBrowser", result.Name);
    }

    [Fact]
    public void ActiveBrowser_WhenBrowserIsEmpty_UpdatesBrowserFieldToFirstAvailable() {
        // Browser = "" triggers the self-heal: Resolve returns the first available, which
        // differs from "" so the field is updated and saved.
        var browserPath = CreateTempApp("FakeBrowser2.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]> { ["FakeBrowser2"] = [browserPath] },
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Browser = "";

        _ = settings.ActiveBrowser;

        Assert.Equal("FakeBrowser2", settings.Browser);
    }

    [Fact]
    public void ActiveBrowser_WhenPreferredBrowserExists_ReturnsItWithoutChanging() {
        var browserPath = CreateTempApp("Safari.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]> { ["Safari"] = [browserPath] },
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Browser = "Safari";

        var result = settings.ActiveBrowser;

        Assert.NotNull(result);
        Assert.Equal("Safari", result.Name);
        Assert.Equal("Safari", settings.Browser);   // no self-heal
    }

    [Fact]
    public void ActiveBrowser_WhenSavedBrowserMissing_SwitchesToFirstAvailableAndSaves() {
        var fallbackPath = CreateTempApp("Chrome.app");
        var platform = new PlatformWithApps(
            // "OldBrowser" has no real path; "Chrome" does
            browserPaths: new Dictionary<string, string[]> {
                ["OldBrowser"] = [Path.Combine(_tempDir, "does_not_exist.app")],
                ["Chrome"]     = [fallbackPath]
            },
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Browser = "OldBrowser";

        var result = settings.ActiveBrowser;

        Assert.NotNull(result);
        Assert.Equal("Chrome", result.Name);
        Assert.Equal("Chrome", settings.Browser);   // self-healed

        // Verify the self-heal was persisted to disk
        var savedJson = File.ReadAllText(_settingsFile);
        using var doc = JsonDocument.Parse(savedJson);
        Assert.Equal("Chrome", doc.RootElement.GetProperty("browser").GetString());
    }

    [Fact]
    public void ActiveBrowser_WhenNoBrowserAvailable_ReturnsNull() {
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Browser = "";

        Assert.Null(settings.ActiveBrowser);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ActiveTerminal auto-repair
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ActiveTerminal_WhenTerminalIsEmpty_ReturnsFirstAvailableTerminal() {
        var terminalPath = CreateTempApp("FakeTerminal.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]> { ["FakeTerminal"] = [terminalPath] });
        var settings = Load(platform);
        settings.Terminal = "";

        var result = settings.ActiveTerminal;

        Assert.NotNull(result);
        Assert.Equal("FakeTerminal", result.Name);
    }

    [Fact]
    public void ActiveTerminal_WhenTerminalIsEmpty_UpdatesTerminalFieldToFirstAvailable() {
        // Terminal = "" triggers the self-heal: Resolve returns the first available, which
        // differs from "" so the field is updated and saved.
        var terminalPath = CreateTempApp("FakeTerminal3.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]> { ["FakeTerminal3"] = [terminalPath] });
        var settings = Load(platform);
        settings.Terminal = "";

        _ = settings.ActiveTerminal;

        Assert.Equal("FakeTerminal3", settings.Terminal);
    }

    [Fact]
    public void ActiveTerminal_WhenPreferredTerminalExists_ReturnsItWithoutChanging() {
        var terminalPath = CreateTempApp("Warp.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]> { ["Warp"] = [terminalPath] });
        var settings = Load(platform);
        settings.Terminal = "Warp";

        var result = settings.ActiveTerminal;

        Assert.NotNull(result);
        Assert.Equal("Warp", result.Name);
        Assert.Equal("Warp", settings.Terminal);
    }

    [Fact]
    public void ActiveTerminal_WhenSavedTerminalMissing_SwitchesToFirstAvailableAndSaves() {
        var fallbackPath = CreateTempApp("iTerm.app");
        var platform = new PlatformWithApps(
            browserPaths: new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]> {
                ["OldTerminal"] = [Path.Combine(_tempDir, "nope.app")],
                ["iTerm"]       = [fallbackPath]
            });
        var settings = Load(platform);
        settings.Terminal = "OldTerminal";

        var result = settings.ActiveTerminal;

        Assert.NotNull(result);
        Assert.Equal("iTerm", result.Name);
        Assert.Equal("iTerm", settings.Terminal);

        var savedJson = File.ReadAllText(_settingsFile);
        using var doc = JsonDocument.Parse(savedJson);
        Assert.Equal("iTerm", doc.RootElement.GetProperty("terminal").GetString());
    }

    [Fact]
    public void ActiveTerminal_WhenNoTerminalAvailable_ReturnsNull() {
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);
        settings.Terminal = "";

        Assert.Null(settings.ActiveTerminal);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EnsureIntegrity()
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EnsureIntegrity_RepairsBothBrowserAndTerminal() {
        var browserPath  = CreateTempApp("EnsureBrowser.app");
        var terminalPath = CreateTempApp("EnsureTerminal.app");
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]> { ["EnsureBrowser"]  = [browserPath]  },
            terminalPaths: new Dictionary<string, string[]> { ["EnsureTerminal"] = [terminalPath] });
        var settings = Load(platform);
        // Simulate stale names that don't exist on disk
        settings.Browser  = "StaleB";
        settings.Terminal = "StaleT";

        settings.EnsureIntegrity();

        Assert.Equal("EnsureBrowser",  settings.Browser);
        Assert.Equal("EnsureTerminal", settings.Terminal);
    }

    [Fact]
    public void EnsureIntegrity_DoesNotThrowWhenNoBrowserOrTerminalInstalled() {
        var platform = new PlatformWithApps(
            browserPaths:  new Dictionary<string, string[]>(),
            terminalPaths: new Dictionary<string, string[]>());
        var settings = Load(platform);

        var ex = Record.Exception(() => settings.EnsureIntegrity());

        Assert.Null(ex);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Default theme detection
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Load_MissingThemeInJson_CallsPlatformDefaultTheme() {
        // JSON without a "theme" key — the deserialised record will have Theme = ""
        WriteSettingsJson("""
            {
                "browser": "",
                "terminal": "",
                "searchFolders": ["/docs"],
                "appDirectories": ["/apps"]
            }
            """);

        var settings = Load(new MinimalPlatform("light-blue"));

        Assert.Equal("light-blue", settings.Theme);
    }

    [Theory]
    [InlineData(true,  "dark-default")]
    [InlineData(false, "light-gray")]
    [InlineData(null,  "dark-default")]
    public void DefaultTheme_BasedOnSystemDarkMode(bool? isDark, string expectedTheme) {
        // PlatformProvider.DefaultTheme() is a virtual with concrete logic — test it directly
        var platform = new DarkModePlatform(isDark);
        Assert.Equal(expectedTheme, platform.DefaultTheme());
    }
}

using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Search.WebSearch;
using Yottacast.Core.Services;
using Yottacast.Ipc.Mapping;
using Yottacast.Ipc.Proto;

namespace Yottacast.Ipc.Tests.Mapping;

public class SettingsMapperTests {
    // Minimal platform provider: no real OS calls, just stubs.
    private sealed class StubPlatform : PlatformProvider {
        public override bool? IsSystemDarkMode() => null;
        public override string DefaultTheme() => "dark-default";
        public override List<string> DefaultAppDirectories() => [];
        public override List<string> DefaultSearchFolders() => [];
        public override Task ScanAppsAsync(Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) => Task.CompletedTask;
        public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) => [];
        public override void LaunchApp(string path) { }
        public override Task SearchFilesAsync(string query, Action<FileResult> onResult, int maxResults, IReadOnlyList<string>? folders, CancellationToken ct) => Task.CompletedTask;
        public override string[] KnownBrowserNames => [];
        public override void OpenUrl(string url, string browserName) { }
        public override string[] KnownTerminalNames => [];
        public override void ExecuteCommand(string command, string terminalName) { }
    }

    private static UserSettings MakeSettings() {
        var tmpPath = Path.GetTempFileName();
        File.WriteAllText(tmpPath, """
            {
              "browser": "Safari",
              "theme": "dark-default",
              "hotkey": "Alt+Space",
              "enableCalculator": true,
              "enableEmoji": false,
              "calculatorCurrencyA": "EUR",
              "calculatorCurrencyB": "USD",
              "calculatorDecimalPlaces": 3,
              "stickyWindow": true,
              "dictionaryPrefix": "define",
              "enableHistory": true,
              "historyMaxItems": 50,
              "keepValueWhenHide": true,
              "keepValueWhenHideDuration": 60
            }
            """);
        return UserSettings.Load(new StubPlatform(), settingsPath: tmpPath);
    }

    [Fact]
    public void ToProto_MapsAllScalarFields() {
        var settings = MakeSettings();

        var msg = SettingsMapper.ToProto(settings);

        Assert.Equal("Safari", msg.Browser);
        Assert.Equal("dark-default", msg.Theme);
        Assert.Equal("Alt+Space", msg.Hotkey);
        Assert.True(msg.EnableCalculator);
        Assert.False(msg.EnableEmoji);
        Assert.Equal("EUR", msg.CalculatorCurrencyA);
        Assert.Equal(3, msg.CalculatorDecimalPlaces);
        Assert.Equal(50, msg.HistoryMaxItems);
    }

    [Fact]
    public void ToProto_NullableWindowPosition_MapsToWrapper() {
        var settings = MakeSettings();
        settings.WindowX = 100;
        settings.WindowY = null;

        var msg = SettingsMapper.ToProto(settings);

        Assert.NotNull(msg.WindowX);
        Assert.Equal(100, msg.WindowX);
        Assert.Null(msg.WindowY);
    }

    [Fact]
    public void ApplyProto_UpdatesSettingsFromMessage() {
        var settings = MakeSettings();
        var msg = SettingsMapper.ToProto(settings);
        msg.Theme = "light-blue";
        msg.EnableEmoji = true;
        msg.CalculatorDecimalPlaces = 5;

        SettingsMapper.ApplyProto(msg, settings);

        Assert.Equal("light-blue", settings.Theme);
        Assert.True(settings.EnableEmoji);
        Assert.Equal(5, settings.CalculatorDecimalPlaces);
    }

    [Fact]
    public void RoundTrip_PreservesData() {
        var settings = MakeSettings();
        settings.Theme = "dark-raycast";
        settings.EnableWebSearch = false;

        var msg = SettingsMapper.ToProto(settings);
        var settings2 = MakeSettings();
        SettingsMapper.ApplyProto(msg, settings2);

        Assert.Equal("dark-raycast", settings2.Theme);
        Assert.False(settings2.EnableWebSearch);
    }

    [Theory]
    [InlineData(SearchSourceVisibility.Disabled, SearchVisibility.Disabled)]
    [InlineData(SearchSourceVisibility.Always, SearchVisibility.Always)]
    [InlineData(SearchSourceVisibility.ModeOnly, SearchVisibility.ModeOnly)]
    public void ToProto_MapsFileSearchVisibility(SearchSourceVisibility source, SearchVisibility expected) {
        var settings = MakeSettings();
        settings.FileSearchVisibility = source;

        var msg = SettingsMapper.ToProto(settings);

        Assert.Equal(expected, msg.FileSearchVisibility);
    }

    [Theory]
    [InlineData(SearchSourceVisibility.Disabled)]
    [InlineData(SearchSourceVisibility.Always)]
    [InlineData(SearchSourceVisibility.ModeOnly)]
    public void RoundTrip_PreservesFileSearchVisibility(SearchSourceVisibility source) {
        var settings = MakeSettings();
        settings.FileSearchVisibility = source;

        var msg = SettingsMapper.ToProto(settings);
        var settings2 = MakeSettings();
        SettingsMapper.ApplyProto(msg, settings2);

        // ModeOnly must not be flattened to Always (regression guard).
        Assert.Equal(source, settings2.FileSearchVisibility);
    }

    [Fact]
    public void SettingsMessage_HasNoClipboardHistoryEnabledField() {
        // The orphaned clipboard_history_enabled proto field (tag 9) was removed and its
        // tag reserved. There must be no generated property for it on the message type.
        var prop = typeof(SettingsMessage).GetProperty("ClipboardHistoryEnabled");
        Assert.Null(prop);
    }

    // Builds a UserSettings with EVERY proto-mapped field set to a non-default value,
    // distinct from each other, so a per-field assert can detect a swapped/dropped field.
    // We start from Load (private ctor) and overwrite via public setters; the values we set
    // are already final, so Load's normalization (trim/distinct/merge) does not interfere.
    private static UserSettings MakeFullySetSettings() {
        var s = MakeSettings();
        s.Browser = "Firefox";
        s.Terminal = "iTerm";
        s.Theme = "light-blue";
        s.Hotkey = "Ctrl+Shift+K";
        s.SearchFolders = ["/a/docs", "/b/projects", "/c/notes"];
        s.AppDirectories = ["/Applications", "/opt/apps"];
        s.EnableAppSearch = false;
        s.EnableCalculator = false;
        s.EnableEmoji = false;
        s.FileSearchVisibility = SearchSourceVisibility.ModeOnly;
        s.EnableWebSearch = false;
        s.ShowDisabledWebSearchEngines = false;
        s.FileSearchOnlySpecificFolders = true;
        s.StickyWindow = false;
        s.CalculatorCurrencyA = "GBP";
        s.CalculatorCurrencyB = "JPY";
        s.CalculatorDecimalPlaces = 7;
        s.CalculatorIncludeMetals = false;
        s.CalculatorIncludeCrypto = true;
        s.ExchangeRateRefreshIntervalHours = 13;
        s.EnableDictionary = false;
        s.DictionaryPrefix = "def";
        s.DictionaryShowAlways = true;
        s.DictionaryLanguages = ["es", "en", "fr"];
        s.EnableHistory = false;
        s.HistoryMaxItems = 42;
        s.KeepValueWhenHide = false;
        s.KeepValueWhenHideDuration = 99;
        s.EnableSystemSettings = false;
        s.WindowX = 111;
        s.WindowY = 222;
        s.WebSearchEngines = [
            new WebSearchEngineSettings { Id = "google", Enabled = true, Mode = WebSearchMode.ShowAlways, Prefix = "g", QueryUrl = "https://g/?q={query}" },
            new WebSearchEngineSettings { Id = "ddg", Enabled = false, Mode = WebSearchMode.PrefixOnly, Prefix = "d", QueryUrl = null },
        ];
        return s;
    }

    [Fact]
    public void ToProto_MapsEveryProtoField() {
        var s = MakeFullySetSettings();

        var msg = SettingsMapper.ToProto(s);

        Assert.Equal("Firefox", msg.Browser);
        Assert.Equal("iTerm", msg.Terminal);
        Assert.Equal("light-blue", msg.Theme);
        Assert.Equal("Ctrl+Shift+K", msg.Hotkey);
        Assert.Equal(new[] { "/a/docs", "/b/projects", "/c/notes" }, msg.SearchFolders);
        Assert.Equal(new[] { "/Applications", "/opt/apps" }, msg.AppDirectories);
        Assert.False(msg.EnableAppSearch);
        Assert.False(msg.EnableCalculator);
        Assert.False(msg.EnableEmoji);
        Assert.Equal(SearchVisibility.ModeOnly, msg.FileSearchVisibility);
        Assert.False(msg.EnableWebSearch);
        Assert.False(msg.ShowDisabledWebSearchEngines);
        Assert.True(msg.FileSearchOnlySpecificFolders);
        Assert.False(msg.StickyWindow);
        Assert.Equal("GBP", msg.CalculatorCurrencyA);
        Assert.Equal("JPY", msg.CalculatorCurrencyB);
        Assert.Equal(7, msg.CalculatorDecimalPlaces);
        Assert.False(msg.CalculatorIncludeMetals);
        Assert.True(msg.CalculatorIncludeCrypto);
        Assert.Equal(13, msg.ExchangeRateRefreshIntervalHours);
        Assert.False(msg.EnableDictionary);
        Assert.Equal("def", msg.DictionaryPrefix);
        Assert.True(msg.DictionaryShowAlways);
        Assert.Equal(new[] { "es", "en", "fr" }, msg.DictionaryLanguages);
        Assert.False(msg.EnableHistory);
        Assert.Equal(42, msg.HistoryMaxItems);
        Assert.False(msg.KeepValueWhenHide);
        Assert.Equal(99, msg.KeepValueWhenHideDuration);
        Assert.False(msg.EnableSystemSettings);
        Assert.Equal(111, msg.WindowX);
        Assert.Equal(222, msg.WindowY);

        Assert.Equal(2, msg.WebSearchEngines.Count);
        Assert.Equal("google", msg.WebSearchEngines[0].Id);
        Assert.True(msg.WebSearchEngines[0].Enabled);
        Assert.Equal((int)WebSearchMode.ShowAlways, msg.WebSearchEngines[0].Mode);
        Assert.Equal("g", msg.WebSearchEngines[0].Prefix);
        Assert.Equal("https://g/?q={query}", msg.WebSearchEngines[0].QueryUrl);
        Assert.Equal("ddg", msg.WebSearchEngines[1].Id);
        Assert.False(msg.WebSearchEngines[1].Enabled);
        Assert.Equal((int)WebSearchMode.PrefixOnly, msg.WebSearchEngines[1].Mode);
        Assert.Equal("d", msg.WebSearchEngines[1].Prefix);
        // null QueryUrl is mapped to "" on the proto side (proto3 has no null string).
        Assert.Equal("", msg.WebSearchEngines[1].QueryUrl);

        // Domain fields intentionally NOT present in the proto (no assert possible because
        // there is no proto field): ClipboardSearchVisibility, ClipboardHotkey,
        // EnableUrlValidation, LastLaunchedVersion, ClipboardHistoryMaxEntries,
        // ClipboardHistoryMaxDays, DateSearchEnabled, DateIsoFormat, DateLongFormat,
        // EnableFileEditor, FileEditorAutoSave, FileEditorExtensions.
        // These are out of scope for the IPC settings contract by design.
    }

    [Fact]
    public void RoundTrip_PreservesEveryProtoField() {
        var s = MakeFullySetSettings();

        var msg = SettingsMapper.ToProto(s);
        var dest = MakeSettings();
        SettingsMapper.ApplyProto(msg, dest);

        Assert.Equal("Firefox", dest.Browser);
        Assert.Equal("iTerm", dest.Terminal);
        Assert.Equal("light-blue", dest.Theme);
        Assert.Equal("Ctrl+Shift+K", dest.Hotkey);
        // Collections must preserve order and content exactly.
        Assert.Equal(new[] { "/a/docs", "/b/projects", "/c/notes" }, dest.SearchFolders);
        Assert.Equal(new[] { "/Applications", "/opt/apps" }, dest.AppDirectories);
        Assert.Equal(new[] { "es", "en", "fr" }, dest.DictionaryLanguages);
        Assert.False(dest.EnableAppSearch);
        Assert.False(dest.EnableCalculator);
        Assert.False(dest.EnableEmoji);
        // Regression guard: ModeOnly must survive the round trip (not flatten to Always).
        Assert.Equal(SearchSourceVisibility.ModeOnly, dest.FileSearchVisibility);
        Assert.False(dest.EnableWebSearch);
        Assert.False(dest.ShowDisabledWebSearchEngines);
        Assert.True(dest.FileSearchOnlySpecificFolders);
        Assert.False(dest.StickyWindow);
        Assert.Equal("GBP", dest.CalculatorCurrencyA);
        Assert.Equal("JPY", dest.CalculatorCurrencyB);
        Assert.Equal(7, dest.CalculatorDecimalPlaces);
        Assert.False(dest.CalculatorIncludeMetals);
        Assert.True(dest.CalculatorIncludeCrypto);
        Assert.Equal(13, dest.ExchangeRateRefreshIntervalHours);
        Assert.False(dest.EnableDictionary);
        Assert.Equal("def", dest.DictionaryPrefix);
        Assert.True(dest.DictionaryShowAlways);
        Assert.False(dest.EnableHistory);
        Assert.Equal(42, dest.HistoryMaxItems);
        Assert.False(dest.KeepValueWhenHide);
        Assert.Equal(99, dest.KeepValueWhenHideDuration);
        Assert.False(dest.EnableSystemSettings);
        Assert.Equal(111, dest.WindowX);
        Assert.Equal(222, dest.WindowY);

        Assert.Equal(2, dest.WebSearchEngines.Count);
        var g = dest.WebSearchEngines[0];
        Assert.Equal("google", g.Id);
        Assert.True(g.Enabled);
        Assert.Equal(WebSearchMode.ShowAlways, g.Mode);
        Assert.Equal("g", g.Prefix);
        Assert.Equal("https://g/?q={query}", g.QueryUrl);
        var d = dest.WebSearchEngines[1];
        Assert.Equal("ddg", d.Id);
        Assert.False(d.Enabled);
        Assert.Equal(WebSearchMode.PrefixOnly, d.Mode);
        Assert.Equal("d", d.Prefix);
        // Empty proto QueryUrl must come back as null in the domain model.
        Assert.Null(d.QueryUrl);
    }

    [Fact]
    public void RoundTrip_ClipboardSearchVisibilityIsNotCarried() {
        // ClipboardSearchVisibility has no proto field, so it cannot survive an IPC round trip.
        // This documents that the IPC contract does not expose it: a destination that already
        // holds a value keeps its own; the source value is simply not transmitted.
        var s = MakeFullySetSettings();
        s.ClipboardSearchVisibility = SearchSourceVisibility.ModeOnly;

        var msg = SettingsMapper.ToProto(s);
        var dest = MakeSettings();
        dest.ClipboardSearchVisibility = SearchSourceVisibility.Disabled;
        SettingsMapper.ApplyProto(msg, dest);

        // dest keeps its own value because the mapper never touches it.
        Assert.Equal(SearchSourceVisibility.Disabled, dest.ClipboardSearchVisibility);
    }

    [Fact]
    public void RoundTrip_EmptyCollections_StayEmpty() {
        var s = MakeSettings();
        s.SearchFolders = [];
        s.AppDirectories = [];
        s.DictionaryLanguages = [];
        s.WebSearchEngines = [];

        var msg = SettingsMapper.ToProto(s);
        var dest = MakeSettings();
        SettingsMapper.ApplyProto(msg, dest);

        Assert.Empty(dest.SearchFolders);
        Assert.Empty(dest.AppDirectories);
        Assert.Empty(dest.DictionaryLanguages);
        Assert.Empty(dest.WebSearchEngines);
    }
}

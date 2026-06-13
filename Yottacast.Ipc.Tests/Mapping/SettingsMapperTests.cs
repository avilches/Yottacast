using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.UserDocuments;
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
}

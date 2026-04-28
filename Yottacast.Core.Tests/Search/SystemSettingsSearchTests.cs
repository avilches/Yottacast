using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.SystemSettings;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class SystemSettingsSearchTests {
    private static (SystemSettingsSearch search, UserSettings settings, TrackingPlatformProvider platform)
        Build(IReadOnlyList<string>? thirdPartyDirs = null) {
        var platform = new TrackingPlatformProvider();
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var search = new SystemSettingsSearch(
            settings, platform, iconCache,
            NullLogger<SystemSettingsSearch>.Instance,
            thirdPartyDirs);
        return (search, settings, platform);
    }

    [Fact]
    public async Task Search_WhenDisabled_ReturnsEmpty() {
        var (search, settings, _) = Build();
        settings.EnableSystemSettings = false;
        search.Start();
        await search.WhenReady();
        var results = search.Search("wifi", 10);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_BuiltinPanel_ExactMatch_ReturnsResult() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("Wi-Fi", 10)
            .Cast<ResultItemViewModel>().ToList();

        Assert.Single(results);
        Assert.Equal("Wi-Fi", results[0].Title);
        Assert.Equal("System Settings", results[0].Subtitle);
        Assert.Equal("System Settings", results[0].Category);
        Assert.Equal(1.0, results[0].Score);
    }

    [Fact]
    public async Task Search_PrefixMatch_MatchesBluetooth() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("blue", 10).Cast<ResultItemViewModel>().ToList();

        Assert.Contains(results, r => r.Title == "Bluetooth");
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("zzzzzzxxx", 10);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_OnActivate_CallsLaunchUrlWithCorrectScheme() {
        var (search, _, platform) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("Bluetooth", 10).Cast<ResultItemViewModel>().ToList();
        var bluetooth = results.First(r => r.Title == "Bluetooth");
        bluetooth.OnActivate?.Invoke();

        Assert.Single(platform.LaunchedUrls);
        Assert.Equal("x-apple.systempreferences:com.apple.preferences.Bluetooth",
            platform.LaunchedUrls[0]);
    }

    [Fact]
    public async Task Search_ThirdPartyPane_AppearsInResults() {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            var bundleContents = Path.Combine(tempDir, "TestPane.prefPane", "Contents");
            Directory.CreateDirectory(bundleContents);
            await File.WriteAllTextAsync(Path.Combine(bundleContents, "Info.plist"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>CFBundleIdentifier</key>
                    <string>com.test.testpane</string>
                    <key>CFBundleDisplayName</key>
                    <string>Test Preference Pane</string>
                </dict>
                </plist>
                """);

            var (search, _, _) = Build(thirdPartyDirs: [tempDir]);
            search.Start();
            await search.WhenReady();

            var results = search.Search("Test", 10).Cast<ResultItemViewModel>().ToList();
            var pane = results.FirstOrDefault(r => r.Title == "Test Preference Pane");

            Assert.NotNull(pane);
            Assert.Equal("System Settings · Preference Pane", pane.Subtitle);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

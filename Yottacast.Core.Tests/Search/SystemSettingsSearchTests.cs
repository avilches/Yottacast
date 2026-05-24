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
        Assert.Equal(4.4, results[0].Score); // exact match: NameMatcher 1.1 × 4
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
        var open = bluetooth.Actions.Single(a => a.Hotkey == ActionHotkey.Enter);
        open.Execute();

        Assert.Single(platform.LaunchedApps);
        Assert.Equal("x-apple.systempreferences:com.apple.preferences.Bluetooth",
            platform.LaunchedApps[0]);
    }

    [Fact]
    public async Task Search_SubItem_ShowsParentInSubtitle() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("Camera", 10).Cast<ResultItemViewModel>().ToList();

        var camera = results.FirstOrDefault(r => r.Title == "Camera");
        Assert.NotNull(camera);
        Assert.Equal("System Settings › Privacy & Security", camera.Subtitle);
    }

    [Fact]
    public async Task Search_TopLevelPanel_KeepsPlainSubtitle() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        var results = search.Search("Bluetooth", 10).Cast<ResultItemViewModel>().ToList();

        var bt = results.First(r => r.Title == "Bluetooth" && r.Subtitle == "System Settings");
        Assert.Equal("System Settings", bt.Subtitle);
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

    // ── Tests de items dinámicos ─────────────────────────────────────────────────

    private static (SystemSettingsSearch search, UserSettings settings)
        BuildWithPlatform(FakePlatformProvider platform, IReadOnlyList<string>? thirdPartyDirs = null) {
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var search = new SystemSettingsSearch(
            settings, platform, iconCache,
            NullLogger<SystemSettingsSearch>.Instance,
            thirdPartyDirs,
            dynamicCacheTtl: TimeSpan.FromHours(1)); // no expira durante el test
        return (search, settings);
    }

    [Fact]
    public async Task Search_WifiConnected_ShowsDynamicItem() {
        var platform = new DynamicFakePlatformProvider { WifiNetwork = "MyHomeWifi" };
        var (search, _) = BuildWithPlatform(platform);
        search.Start();
        await search.WhenReady();

        var results = search.Search("MyHomeWifi", 10).Cast<ResultItemViewModel>().ToList();

        Assert.Single(results);
        Assert.Equal("Wi-Fi · MyHomeWifi", results[0].Title);
        Assert.Equal("System Settings › Network", results[0].Subtitle);
    }

    [Fact]
    public async Task Search_WifiDisconnected_NoDynamicWifiItem() {
        var platform = new DynamicFakePlatformProvider { WifiNetwork = null };
        var (search, _) = BuildWithPlatform(platform);
        search.Start();
        await search.WhenReady();

        var results = search.Search("Wi-Fi", 10).Cast<ResultItemViewModel>().ToList();

        Assert.DoesNotContain(results, r => r.Title.StartsWith("Wi-Fi ·"));
    }

    [Fact]
    public async Task Search_VpnActive_ShowsDynamicVpnItem() {
        var platform = new DynamicFakePlatformProvider { VpnNames = ["Work VPN"] };
        var (search, _) = BuildWithPlatform(platform);
        search.Start();
        await search.WhenReady();

        var results = search.Search("Work VPN", 10).Cast<ResultItemViewModel>().ToList();

        Assert.Single(results);
        Assert.Equal("VPN · Work VPN", results[0].Title);
    }

    [Fact]
    public async Task Search_DynamicItems_RefreshedOnceDuringStartup() {
        // El refresco dinámico ocurre en background durante Start(); las llamadas a
        // Search() solo leen del cache. Con TTL<=0 el bucle de refresco queda deshabilitado.
        var platform = new CountingDynamicProvider { WifiNetwork = "TestNet" };
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var search = new SystemSettingsSearch(
            settings, platform, iconCache,
            NullLogger<SystemSettingsSearch>.Instance,
            thirdPartyDirs: [],
            dynamicCacheTtl: TimeSpan.Zero);
        search.Start();
        await search.WhenReady();

        _ = search.Search("TestNet", 10);
        _ = search.Search("TestNet", 10);

        Assert.Equal(1, platform.WifiCallCount);
    }

    [Fact]
    public async Task Search_DynamicItems_BackgroundRefreshLoop() {
        // Con un TTL pequeño, el bucle background refresca el cache periódicamente
        // sin que las llamadas a Search() disparen subprocess.
        var platform = new CountingDynamicProvider { WifiNetwork = "TestNet" };
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var search = new SystemSettingsSearch(
            settings, platform, iconCache,
            NullLogger<SystemSettingsSearch>.Instance,
            thirdPartyDirs: [],
            dynamicCacheTtl: TimeSpan.FromMilliseconds(50));
        search.Start();
        await search.WhenReady();
        var initialCount = platform.WifiCallCount;

        await Task.Delay(250);
        await search.Stop();

        Assert.True(platform.WifiCallCount > initialCount,
            $"Expected background refresh to increment WifiCallCount, got {platform.WifiCallCount} (started at {initialCount})");
    }

    [Fact(Skip = "manual — abre System Settings para verificar visualmente cada anchor")]
    public async Task Manual_AllAnchorsOpen() {
        var (search, _, _) = Build();
        search.Start();
        await search.WhenReady();

        // Busca términos representativos de sub-secciones del catálogo para abrirlas una a una
        var probes = new[] {
            "Camera", "Microphone", "Location Services", "Full Disk Access", "FileVault", "Firewall",
            "Keyboard Shortcuts", "Night Shift", "Hot Corners", "Login Items", "AirDrop",
            "Time Zone", "Screen Sharing", "VoiceOver", "Battery Options",
        };
        foreach (var query in probes) {
            var results = search.Search(query, 1).Cast<ResultItemViewModel>().ToList();
            foreach (var r in results) r.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter)?.Execute();
            await Task.Delay(1200);
        }
    }

    // ── Fakes para tests dinámicos ───────────────────────────────────────────────

    private sealed class DynamicFakePlatformProvider : FakePlatformProvider {
        public string? WifiNetwork { get; init; }
        public IReadOnlyList<string> VpnNames { get; init; } = [];
        public DynamicFakePlatformProvider() : base([]) { }
        public override string? GetCurrentWifiNetworkName() => WifiNetwork;
        public override IReadOnlyList<string> GetActiveVpnNames() => VpnNames;
    }

    private sealed class CountingDynamicProvider : FakePlatformProvider {
        public string? WifiNetwork { get; set; }
        public int WifiCallCount { get; private set; }
        public CountingDynamicProvider() : base([]) { }
        public override string? GetCurrentWifiNetworkName() { WifiCallCount++; return WifiNetwork; }
        public override IReadOnlyList<string> GetActiveVpnNames() => [];
    }
}

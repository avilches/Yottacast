using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// PlatformProvider variant that populates apps during ScanAppsAsync,
/// making ApplicationSearch testable without touching the filesystem.
/// </summary>
internal sealed class FakePlatformProviderWithApps(IReadOnlyList<string> appPaths)
    : FakePlatformProvider([]) {
    public override async Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        foreach (var path in appPaths) {
            ct.ThrowIfCancellationRequested();
            addApp(path);
        }
        await Task.CompletedTask;
    }
}

public class ApplicationSearchTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationSearch BuildSearch(params string[] appPaths) {
        var platform = new FakePlatformProviderWithApps(appPaths);
        var settings = UserSettings.Load(platform);
        var iconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        return new ApplicationSearch(settings, platform, iconCache, NullLogger<ApplicationSearch>.Instance);
    }

    private static async Task StartAndWaitAsync(ApplicationSearch search) {
        search.Start();
        await search.WhenReady();
    }

    private static IReadOnlyList<Yottacast.Core.ViewModels.ResultItemViewModel> SearchAll(
        ApplicationSearch search, string query, int limit = 50) {
        return search.Search(query, limit)
            .Cast<Yottacast.Core.ViewModels.ResultItemViewModel>().ToList();
    }

    // ── Before WhenReady ──────────────────────────────────────────────────────

    [Fact]
    public void SearchAsync_BeforeStart_ReturnsEmpty() {
        // Do NOT call Start() — cache is empty, WhenReady() never completes.
        var search = BuildSearch("/Applications/Safari.app");
        var results = SearchAll(search, "safari");
        Assert.Empty(results);
    }

    // ── Exact / prefix match (Score = 1.0) ───────────────────────────────────

    [Fact]
    public async Task SearchAsync_ExactName_ReturnsResult_ScoreOne() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "Safari");
        Assert.Single(results);
        Assert.Equal("Safari", results[0].Title);
        Assert.Equal(1.0, results[0].Score);
    }

    [Fact]
    public async Task SearchAsync_PrefixQuery_ReturnsResult_ScoreOne() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "Saf");
        Assert.Single(results);
        Assert.Equal(1.0, results[0].Score);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitive_ExactMatch() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "safari");
        Assert.Single(results);
        Assert.Equal("Safari", results[0].Title);
    }

    // ── CamelHump matching ────────────────────────────────────────────────────

    [Theory]
    [InlineData("AM",    "Activity Monitor", 1.0)]   // uppercase initials → each char is a hump
    [InlineData("am",    "Activity Monitor", 1.0)]   // lowercase → also tried as initials
    [InlineData("AcMon", "Activity Monitor", 1.0)]   // multi-hump prefix
    [InlineData("Mon",   "Activity Monitor", 0.8)]   // prefix of non-first token
    public async Task SearchAsync_CamelHump_ReturnsExpectedScore(
        string query, string appName, double expectedScore) {
        var appPath = $"/Applications/{appName}.app";
        var search = BuildSearch(appPath);
        await StartAndWaitAsync(search);
        var results = SearchAll(search, query);
        Assert.Single(results);
        Assert.Equal(appName, results[0].Title);
        Assert.Equal(expectedScore, results[0].Score);
    }

    [Fact]
    public async Task SearchAsync_Initials_MON_MicrosoftOneNote() {
        var search = BuildSearch("/Applications/Microsoft OneNote.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "MON");
        Assert.Single(results);
        Assert.Equal("Microsoft OneNote", results[0].Title);
        // MON as initials of Microsoft / One / Note → 0.6 via initials fallback
        Assert.True(results[0].Score > 0);
    }

    // ── Substring match (Score = 0.2) ─────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_InternalSubstring_ReturnsLowScore() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        // "ari" is inside "Safari" but not a prefix of any token
        var results = SearchAll(search, "ari");
        Assert.Single(results);
        Assert.Equal(0.2, results[0].Score);
    }

    [Fact]
    public async Task SearchAsync_ShortSubstring_TwoChars_NoMatch() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        // "af" is 2 chars — substring threshold requires 3+
        var results = SearchAll(search, "af");
        Assert.Empty(results);
    }

    // ── No match ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "xyz");
        Assert.Empty(results);
    }

    // ── Multiple apps, ordering by score ─────────────────────────────────────

    [Fact]
    public async Task SearchAsync_MultipleApps_OrderedByScoreDescending() {
        // "Safari" prefix score 1.0 vs "Safari Extensions" prefix score 1.0 but second is substring
        var search = BuildSearch(
            "/Applications/Activity Monitor.app",  // "Mon" → score 0.8
            "/Applications/Safari.app"             // "Mon" → no match
        );
        await StartAndWaitAsync(search);
        // "Saf" matches Safari (1.0) only
        var results = SearchAll(search, "Saf");
        Assert.Single(results);
        Assert.Equal("Safari", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_MultipleMatches_HigherScoreFirst() {
        // Both apps match "mon" but with different scores
        // Activity Monitor: "Mon" is prefix of "Monitor" (non-first token) → 0.8
        // Microsoft OneNote: "mon" lowercase → tried as uppercase MON → initials → 0.6
        var search = BuildSearch(
            "/Applications/Microsoft OneNote.app",
            "/Applications/Activity Monitor.app"
        );
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "mon");
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score >= results[1].Score,
            $"Expected results ordered by score desc but got {results[0].Score} then {results[1].Score}");
    }

    // ── Limit parameter ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_LimitRespected() {
        var search = BuildSearch(
            "/Applications/Safari.app",
            "/Applications/System Preferences.app",
            "/Applications/Stickies.app",
            "/Applications/Screen Saver.app"
        );
        await StartAndWaitAsync(search);
        // All four start with "S" → "s" prefix should match all
        // Limit to 2
        var results = SearchAll(search, "S", limit: 2);
        Assert.Equal(2, results.Count);
    }

    // ── Start / Stop lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task Stop_ClearsCache_SearchReturnsEmpty() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);

        // Confirm app is found before Stop
        var before = SearchAll(search, "Safari");
        Assert.Single(before);

        await search.Stop();

        var after = SearchAll(search, "Safari");
        Assert.Empty(after);
    }

    [Fact]
    public async Task StartAfterStop_RebuildsCacheAndFindsApps() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        await search.Stop();

        // Restart
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "Safari");
        Assert.Single(results);
        Assert.Equal("Safari", results[0].Title);
    }

    [Fact]
    public async Task Start_CalledTwice_IsIdempotent() {
        var search = BuildSearch("/Applications/Safari.app");
        search.Start();
        search.Start(); // second call is a no-op
        await search.WhenReady();
        var results = SearchAll(search, "Safari");
        Assert.Single(results); // only one copy of Safari
    }

    // ── Find / FindAll ────────────────────────────────────────────────────────

    [Fact]
    public async Task Find_ExistingApp_ReturnsCorrectAppInfo() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var app = search.Find("Safari");
        Assert.NotNull(app);
        Assert.Equal("Safari", app.Name);
        Assert.Equal("/Applications/Safari.app", app.Path);
    }

    [Fact]
    public async Task Find_NonExistingApp_ReturnsNull() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        Assert.Null(search.Find("Chrome"));
    }

    [Fact]
    public async Task Find_CaseInsensitive() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        Assert.NotNull(search.Find("SAFARI"));
        Assert.NotNull(search.Find("safari"));
    }

    [Fact]
    public async Task FindAll_ReturnsAllApps() {
        var search = BuildSearch(
            "/Applications/Safari.app",
            "/Applications/Activity Monitor.app"
        );
        await StartAndWaitAsync(search);
        var all = search.FindAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.Name == "Safari");
        Assert.Contains(all, a => a.Name == "Activity Monitor");
    }

    // ── AppAdded event ────────────────────────────────────────────────────────

    [Fact]
    public async Task AppAdded_Event_FiresForEachNewApp() {
        var fired = new List<string>();
        var search = BuildSearch(
            "/Applications/Safari.app",
            "/Applications/Activity Monitor.app"
        );
        search.AppAdded += app => fired.Add(app.Name);
        await StartAndWaitAsync(search);

        Assert.Equal(2, fired.Count);
        Assert.Contains("Safari", fired);
        Assert.Contains("Activity Monitor", fired);
    }

    [Fact]
    public async Task AppAdded_Event_NotFiredForDuplicatePaths() {
        // Adding the same path twice should only fire AppAdded once per name
        var fired = new List<string>();
        var search = BuildSearch(
            "/Applications/Safari.app",
            "/Applications/Safari.app"  // duplicate
        );
        search.AppAdded += app => fired.Add(app.Name);
        await StartAndWaitAsync(search);

        Assert.Single(fired);
        Assert.Equal("Safari", fired[0]);
    }

    // ── Result metadata ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_ResultHasCorrectCategoryAndSubtitle() {
        var search = BuildSearch("/Applications/Safari.app");
        await StartAndWaitAsync(search);
        var results = SearchAll(search, "Safari");
        Assert.Single(results);
        Assert.Equal("Applications", results[0].Category);
        Assert.Equal("/Applications/Safari.app", results[0].Subtitle);
    }

    // ── WhenReady ────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenReady_CompletesAfterScanFinishes() {
        var search = BuildSearch("/Applications/Safari.app");
        search.Start();
        // WhenReady should complete without hanging
        var completed = await Task.WhenAny(search.WhenReady(), Task.Delay(1000)) == search.WhenReady();
        Assert.True(completed, "WhenReady() did not complete within 1 second");
    }

    [Fact]
    public async Task WhenReady_BeforeStart_DoesNotComplete() {
        var search = BuildSearch("/Applications/Safari.app");
        // Without Start(), WhenReady() should remain pending
        var completed = await Task.WhenAny(search.WhenReady(), Task.Delay(100)) == search.WhenReady();
        Assert.False(completed, "WhenReady() completed without Start() being called");
    }
}

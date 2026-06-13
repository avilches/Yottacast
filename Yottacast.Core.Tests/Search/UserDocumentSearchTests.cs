using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class UserDocumentSearchTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UserDocumentSearch BuildSearch(params FileResult[] files) {
        var platform = new FakePlatformProvider(files);
        var settings = UserSettings.Load(platform);
        var fileSearch = new FileSearch(platform);
        var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
        return new UserDocumentSearch(settings, fileSearch, fileIconCache, platform, NullLogger<UserDocumentSearch>.Instance, new ClipboardService(NullLogger<ClipboardService>.Instance));
    }

    private static UserDocumentSearch BuildSearch(ClipboardService clipboard, params FileResult[] files) {
        var platform = new FakePlatformProvider(files);
        var settings = UserSettings.Load(platform);
        var fileSearch = new FileSearch(platform);
        var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
        return new UserDocumentSearch(settings, fileSearch, fileIconCache, platform, NullLogger<UserDocumentSearch>.Instance, clipboard);
    }

    /// <summary>Collects all snapshots and returns the last one (final state).</summary>
    private static async Task<IReadOnlyList<ResultItemViewModel>> SearchAllAsync(
        UserDocumentSearch search, string query, int limit = 20) {
        IReadOnlyList<BaseResultItemViewModel> last = [];
        await foreach (var snapshot in search.SearchAsync(query, limit))
            last = snapshot;
        return last.Cast<ResultItemViewModel>().ToList();
    }

    // ── Single file ───────────────────────────────────────────────────────────

    // fileName | filePath | query | expectedCount | expectedScore
    public static TheoryData<string, string, string, int, double> SingleFileCases => new() {
        { "report.pdf", "/docs/report.pdf", "report",      1, 3.5   },  // exact stem match (1.0 × 3.5)
        { "report",     "/docs/report",     "report",      1, 2.975 },  // exact name match (0.85 × 3.5)
        { "report.pdf", "/docs/report.pdf", "rep",         1, 2.625 },  // stem starts with (0.75 × 3.5)
        { "report.pdf", "/docs/report.pdf", "epor",        1, 1.75  },  // substring only (0.5 × 3.5)
        { "abc.txt",    "/abc.txt",         "a",           0, 0.0   },  // too short → empty
        { "mis calculos.xls", "/docs/mis calculos.xls", "xls calc mis", 1, 2.625 },  // multi: all prefixes (0.75 × 3.5)
        { "mis calculos.xls", "/docs/mis calculos.xls", "lcul mis xls", 1, 1.75  },  // multi: substring token (0.5 × 3.5)
        { "mis calculos.xls", "/docs/mis calculos.xls", "mis zzz",      0, 0.0   },  // multi: missing token
    };

    [Theory]
    [MemberData(nameof(SingleFileCases))]
    public async Task SingleFile_ScoreAndCount(
        string fileName, string filePath, string query, int expectedCount, double expectedScore) {
        var results = await SearchAllAsync(BuildSearch(new FileResult(fileName, filePath)), query);
        Assert.Equal(expectedCount, results.Count);
        if (expectedCount > 0)
            Assert.Equal(expectedScore, results[0].Score);
    }

    // ── Multiple files ────────────────────────────────────────────────────────

    // files | query | expectedCount | expectedFirstTitle
    public static TheoryData<FileResult[], string, int, string> MultiFileCases => new() {
        // "otros datos.xlsx" lacks "mis" and "calc" → filtered out, only 1 result
        {
            [new("mis calculos.xls", "/docs/mis calculos.xls"), new("otros datos.xlsx", "/docs/otros datos.xlsx")],
            "xls calc mis", 1, "mis calculos.xls"
        },
        // "mis calculos.xls" (score 0.75, all prefixes) sorts before "amis acalc.xls" (score 0.5, substrings)
        {
            [new("amis acalc.xls", "/docs/amis acalc.xls"), new("mis calculos.xls", "/docs/mis calculos.xls")],
            "mis calc xls", 2, "mis calculos.xls"
        },
    };

    [Theory]
    [MemberData(nameof(MultiFileCases))]
    public async Task MultiFile_CountAndFirstTitle(
        FileResult[] files, string query, int expectedCount, string expectedFirstTitle) {
        var results = await SearchAllAsync(BuildSearch(files), query);
        Assert.Equal(expectedCount, results.Count);
        Assert.Equal(expectedFirstTitle, results[0].Title);
    }

    // ── OnCopy ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Results_HaveOnCopyAndCopiedMessage() {
        string? copied = null;
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(
            copy: t => copied = t,
            read: () => Task.FromResult<string?>(null));
        var search = BuildSearch(clipboard, new FileResult("report.pdf", "/docs/report.pdf"));
        var results = await SearchAllAsync(search, "report");
        var item = Assert.Single(results);
        var copy = item.Actions.Single(a => a.Hotkey == ActionHotkey.MetaC);
        Assert.Equal("Path copied!", copy.HintProvider?.Invoke());
        copy.Execute();
        Assert.Equal("/docs/report.pdf", copied);
    }

    // ── Drag payload ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Results_HaveGetDragPayload_ReturnsFileWithPath() {
        var search = BuildSearch(new FileResult("report.pdf", "/docs/report.pdf"));
        var results = await SearchAllAsync(search, "report");
        var item = Assert.Single(results);
        Assert.NotNull(item.GetDragPayload);
        var payload = item.GetDragPayload!();
        var file = Assert.IsType<DragPayload.File>(payload);
        Assert.Equal("/docs/report.pdf", file.AbsolutePath);
    }

    // ── Order independence ────────────────────────────────────────────────────

    [Fact]
    public async Task MultiToken_OrderIndependent_SameTitleAndScore() {
        var file = new FileResult("mis calculos.xls", "/docs/mis calculos.xls");
        var r1 = await SearchAllAsync(BuildSearch(file), "mis calc xls");
        var r2 = await SearchAllAsync(BuildSearch(file), "xls mis calc");
        Assert.Single(r1);
        Assert.Single(r2);
        Assert.Equal(r1[0].Title, r2[0].Title);
        Assert.Equal(r1[0].Score, r2[0].Score);
    }

    // ── Stream resilience ─────────────────────────────────────────────────────

    /// <summary>PlatformProvider whose SearchFilesAsync emits some results, then throws a non-OCE.</summary>
    private sealed class ThrowingPlatformProvider(IReadOnlyList<FileResult> files) : FakePlatformProvider(files) {
        public override Task SearchFilesAsync(
            string query, Action<FileResult> onResult, int maxResults,
            IReadOnlyList<string>? folders, CancellationToken ct) {
            foreach (var f in files)
                onResult(f);
            throw new InvalidOperationException("backend exploded");
        }
    }

    [Fact]
    public async Task SearchAsync_BackendThrows_StreamCompletesWithPartialResults() {
        var platform = new ThrowingPlatformProvider([new FileResult("report.pdf", "/docs/report.pdf")]);
        var settings = UserSettings.Load(platform);
        var search = new UserDocumentSearch(settings, new FileSearch(platform),
            new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
            platform, NullLogger<UserDocumentSearch>.Instance,
            new ClipboardService(NullLogger<ClipboardService>.Instance));

        // The stream must terminate (not hang) even though the backend threw a non-OCE,
        // and it must still emit the final snapshot with the results buffered before the throw.
        var enumerate = SearchAllAsync(search, "report");
        var completed = await Task.WhenAny(enumerate, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(enumerate, completed); // did not time out → channel was completed
        var results = await enumerate;
        var item = Assert.Single(results);
        Assert.Equal("report.pdf", item.Title);
    }

    // ── ISearchModeSource ────────────────────────────────────────────────────────

    [Fact]
    public void IsActiveIn_All_WhenAlways() {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.FileSearchVisibility = SearchSourceVisibility.Always;
        var search = new UserDocumentSearch(settings, new FileSearch(platform),
            new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
            platform, NullLogger<UserDocumentSearch>.Instance,
            new ClipboardService(NullLogger<ClipboardService>.Instance));

        Assert.True(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
    }

    [Fact]
    public void IsActiveIn_Files_WhenModeOnly() {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.FileSearchVisibility = SearchSourceVisibility.ModeOnly;
        var search = new UserDocumentSearch(settings, new FileSearch(platform),
            new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
            platform, NullLogger<UserDocumentSearch>.Instance,
            new ClipboardService(NullLogger<ClipboardService>.Instance));

        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
        Assert.True(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Clipboard));
    }

    [Fact]
    public void IsActiveIn_NeverActive_WhenDisabled() {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.FileSearchVisibility = SearchSourceVisibility.Disabled;
        var search = new UserDocumentSearch(settings, new FileSearch(platform),
            new FileIconCache(platform, NullLogger<FileIconCache>.Instance),
            platform, NullLogger<UserDocumentSearch>.Instance,
            new ClipboardService(NullLogger<ClipboardService>.Instance));

        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.All));
        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Files));
        Assert.False(((ISearchModeSource)search).IsActiveIn(SearchMode.Clipboard));
    }
}

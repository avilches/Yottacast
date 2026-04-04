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
        return new UserDocumentSearch(settings, fileSearch, NullLogger<UserDocumentSearch>.Instance);
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
        { "report.pdf", "/docs/report.pdf", "report",      1, 1.0  },  // exact stem match
        { "report",     "/docs/report",     "report",      1, 1.0  },  // exact name match
        { "report.pdf", "/docs/report.pdf", "rep",         1, 0.75 },  // stem starts with
        { "report.pdf", "/docs/report.pdf", "epor",        1, 0.5  },  // substring only
        { "abc.txt",    "/abc.txt",         "a",           0, 0.0  },  // too short → empty
        { "mis calculos.xls", "/docs/mis calculos.xls", "xls calc mis", 1, 0.75 },  // multi: all prefixes
        { "mis calculos.xls", "/docs/mis calculos.xls", "lcul mis xls", 1, 0.5  },  // multi: substring token
        { "mis calculos.xls", "/docs/mis calculos.xls", "mis zzz",      0, 0.0  },  // multi: missing token
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
}

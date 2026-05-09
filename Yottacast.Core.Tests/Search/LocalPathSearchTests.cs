using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class LocalPathSearchTests {

    private static LocalPathSearch BuildSearch() {
        var platform = new FakePlatformProvider([]);
        var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        return new LocalPathSearch(fileIconCache, platform, clipboard, NullLogger<LocalPathSearch>.Instance);
    }

    // ── IsLocalPath ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Users/foo/bar.txt", true)]
    [InlineData("/tmp", true)]
    [InlineData("/", true)]
    [InlineData("~/Desktop/test.pdf", true)]
    [InlineData("~/", true)]
    [InlineData("~", true)]
    [InlineData("./relative/path", true)]
    [InlineData("../parent/path", true)]
    [InlineData("C:\\Windows\\System32", true)]
    [InlineData("D:\\some\\path.exe", true)]
    [InlineData("hello world", false)]
    [InlineData("google.com", false)]
    [InlineData("https://example.com", false)]
    [InlineData("report.pdf", false)]
    [InlineData("", false)]
    [InlineData("a", false)]
    public void IsLocalPath_DetectsCorrectly(string query, bool expected) {
        Assert.Equal(expected, LocalPathSearch.IsLocalPath(query));
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_NonPath_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("hello", 10));
        Assert.Empty(search.Search("google.com", 10));
        Assert.Empty(search.Search("report.pdf", 10));
    }

    [Fact]
    public void Search_NonExistentPath_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("/this/path/absolutely/does/not/exist_xyz.txt", 10));
    }

    [Fact]
    public void Search_ExistingFile_ReturnsOneResult() {
        var tempFile = Path.GetTempFileName();
        try {
            var search = BuildSearch();
            var results = search.Search(tempFile, 10);
            Assert.Single(results);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.Equal(Path.GetFileName(tempFile), r.Title);
            Assert.Equal(tempFile, r.Subtitle);
            Assert.Equal("Files", r.Category);
            Assert.Equal(10.0, r.Score);
        } finally {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Search_ExistingDirectory_ReturnsOneResult() {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try {
            var search = BuildSearch();
            var results = search.Search(tempDir, 10);
            Assert.Single(results);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.Equal(Path.GetFileName(tempDir), r.Title);
            Assert.Equal(tempDir, r.Subtitle);
        } finally {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void Search_TildeExpansion_ResolvesHomePath() {
        var search = BuildSearch();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var results = search.Search("~/", 10);
        if (!Directory.Exists(home)) return;
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal(home, r.Subtitle);
    }

    [Fact]
    public void Search_ExistingPath_HasActivateAndCopyCallbacks() {
        var tempFile = Path.GetTempFileName();
        try {
            var search = BuildSearch();
            var results = search.Search(tempFile, 10);
            var r = Assert.IsType<ResultItemViewModel>(results[0]);
            Assert.NotNull(r.OnActivate);
            Assert.NotNull(r.OnCopy);
            Assert.Equal("Path copied!", r.CopiedMessage);
        } finally {
            File.Delete(tempFile);
        }
    }
}

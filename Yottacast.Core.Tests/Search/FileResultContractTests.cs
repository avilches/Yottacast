using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Clipboard;
using Yottacast.Core.Search.LocalPath;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Contract: any result with Category="Files" must be FileResultItemViewModel with non-null ItemPath.
/// These tests catch search sources that forget to use the required subtype.
/// </summary>
public class FileResultContractTests {

    [Fact]
    public void LocalPathSearch_FileResult_IsFileResultItemViewModelWithItemPath() {
        var tempFile = Path.GetTempFileName();
        try {
            var platform = new FakePlatformProvider([]);
            var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
            var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
            var search = new LocalPathSearch(fileIconCache, platform, clipboard, NullLogger<LocalPathSearch>.Instance);

            var results = search.Search(tempFile, 10);

            foreach (var result in results.OfType<ResultItemViewModel>().Where(r => r.Category == "Files")) {
                var file = Assert.IsType<FileResultItemViewModel>(result);
                Assert.False(string.IsNullOrEmpty(file.ItemPath),
                    $"'{file.Title}' has Category='Files' but empty ItemPath");
            }
        } finally {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ClipboardSearch_FileResult_IsFileResultItemViewModelWithItemPath() {
        var tempFile = Path.GetTempFileName();
        try {
            var platform = new FakePlatformProvider([]);
            var settings = UserSettings.Load(platform);
            var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
            var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
            var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            var fileIconCache = new FileIconCache(platform, NullLogger<FileIconCache>.Instance);
            var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
            var search = new ClipboardSearch(settings, browserDiscovery, faviconCache, fileIconCache,
                platform, clipboard, NullLogger<ClipboardSearch>.Instance);

            search.OnWindowShown(tempFile);
            var results = search.GetResults();

            foreach (var result in results.OfType<ResultItemViewModel>().Where(r => r.Category == "Files")) {
                var file = Assert.IsType<FileResultItemViewModel>(result);
                Assert.False(string.IsNullOrEmpty(file.ItemPath),
                    $"'{file.Title}' has Category='Files' but empty ItemPath");
            }
        } finally {
            File.Delete(tempFile);
        }
    }
}

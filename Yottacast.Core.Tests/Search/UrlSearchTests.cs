using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class UrlSearchTests {

    // ── TryNormalizeUrl ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com",       "https://example.com",       true)]
    [InlineData("http://example.com",        "http://example.com",        true)]
    [InlineData("https://example.com/path",  "https://example.com/path",  true)]
    [InlineData("www.example.com",           "https://www.example.com",   true)]
    [InlineData("www.example.com/path",      "https://www.example.com/path", true)]
    [InlineData("github.com/user/repo",      "https://github.com/user/repo", true)]
    [InlineData("example.io",               "https://example.io",         true)]
    [InlineData("example.dev",              "https://example.dev",        true)]
    [InlineData("myapp.ai",                 "https://myapp.ai",           true)]
    [InlineData("amazon.us",               "https://amazon.us",          true)]
    [InlineData("bbc.co.uk",               "https://bbc.co.uk",          true)]
    [InlineData("hello world",              "",                           false)]  // tiene espacios
    [InlineData("hello",                    "",                           false)]  // sin punto
    [InlineData("report.pdf",              "",                            false)]  // TLD desconocido
    [InlineData("example.xyz",             "https://example.xyz",         true)]   // TLD IANA válido
    [InlineData("",                         "",                           false)]
    [InlineData("abc",                      "",                           false)]
    [InlineData("/usr/local/bin",           "",                           false)]  // ruta local
    [InlineData("http://",                  "",                           false)]  // sin host
    [InlineData("https://",                 "",                           false)]  // sin host
    [InlineData("http://:8080",             "",                           false)]  // sin host, solo puerto
    // "www." tiene host "www." (no vacio), asi que se acepta: candidato valido para new Uri,
    // sin crash. La verificacion de alcanzabilidad (DNS) lo descartara despues si no resuelve.
    [InlineData("www.",                     "https://www.",               true)]
    public void TryNormalizeUrl_CorrectlyClassifies(
        string query, string expectedUrl, bool expectedResult) {
        var result = UrlSearch.TryNormalizeUrl(query, out var url);
        Assert.Equal(expectedResult, result);
        if (expectedResult) Assert.Equal(expectedUrl, url);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UrlSearch BuildSearch(HttpStatusCode headStatusCode = HttpStatusCode.OK) {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.EnableWebSearch = true;
        settings.EnableUrlValidation = true;
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        return new UrlSearch(settings, browserDiscovery, appIconCache,
            faviconCache, NullLogger<UrlSearch>.Instance);
    }

    /// <summary>Espera a que ResultChanged se dispare (máx <paramref name="timeoutMs"/> ms).</summary>
    private static Task WaitForResultChangedAsync(UrlSearch search, int timeoutMs = 3000) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        search.ResultChanged += () => tcs.TrySetResult();
        return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
    }

    // ── Search behavior ───────────────────────────────────────────────────────

    [Fact]
    public void Search_WebSearchDisabled_ReturnsEmpty() {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.EnableWebSearch = false;
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var search = new UrlSearch(settings, browserDiscovery, appIconCache,
            faviconCache, NullLogger<UrlSearch>.Instance);

        Assert.Empty(search.Search("https://example.com", 10));
        Assert.Empty(search.Search("github.com/user/repo", 10));
    }

    [Fact]
    public void Search_NonUrl_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("hello world", 10));
        Assert.Empty(search.Search("just text", 10));
        Assert.Empty(search.Search("/local/path", 10));
    }

    [Theory]
    [InlineData("h")]
    [InlineData("ht")]
    [InlineData("http")]
    [InlineData("http:")]
    [InlineData("http:/")]
    [InlineData("http://")]
    [InlineData("https://")]
    public void Search_PartialOrHostlessScheme_ReturnsEmptyWithoutThrowing(string query) {
        // Regresión: "http://" sin host hacía que new Uri(url).Host lanzara
        // UriFormatException, matando en silencio la búsqueda de ese keystroke.
        var search = BuildSearch();
        var results = search.Search(query, 10);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_UrlLooking_ReturnsPendingResultImmediately() {
        var search = BuildSearch();
        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://example.com", r.Title);
        Assert.Equal("Web", r.Category);
        Assert.Equal(10.0, r.Score);
        Assert.NotEmpty(r.Actions);
    }

    [Fact]
    public async Task Search_AfterDnsResolved_StillReturnsResult() {
        // example.com always resolves via DNS — result stays Valid after DNS check
        var search = BuildSearch(HttpStatusCode.OK);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Single(search.Search("https://example.com", 10));
    }

    [Fact]
    public void Search_ValidationOff_ReturnsPendingResult() {
        // EnableUrlValidation = false: URL result shown immediately without DNS/HEAD checks
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.EnableWebSearch = true;
        settings.EnableUrlValidation = false;
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var search = new UrlSearch(settings, browserDiscovery, appIconCache,
            faviconCache, NullLogger<UrlSearch>.Instance);

        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://example.com", r.Title);
        Assert.Equal("Web", r.Category);

        // No DNS/HEAD calls — only favicon goes through faviconCache's own handler
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Search_SameUrlTwice_DoesNotStartTwoBackgroundChecks() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.EnableWebSearch = true;
        settings.EnableUrlValidation = true;
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var faviconHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var faviconCache = new FaviconCache(new HttpClient(faviconHandler), NullLogger<FaviconCache>.Instance,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var search = new UrlSearch(settings, browserDiscovery, appIconCache,
            faviconCache, NullLogger<UrlSearch>.Instance);

        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);

        // UrlSearch itself makes no HTTP calls (DNS is not HTTP; favicon is handled by FaviconCache)
        Thread.Sleep(50);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Search_WwwPrefix_NormalizesUrl() {
        var search = BuildSearch();
        var results = search.Search("www.example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://www.example.com", r.Title);
    }

    [Fact]
    public void Search_Subtitle_ContainsBrowserName() {
        var search = BuildSearch();
        var results = search.Search("https://example.com", 10);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        // ActiveBrowser es null en test (FakePlatformProvider sin browsers)
        Assert.Equal("Open in browser", r.Subtitle);
    }

    [Fact]
    public async Task Stop_ClearsState() {
        var search = BuildSearch(HttpStatusCode.OK);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;

        await search.Stop();

        // Después de Stop, una nueva búsqueda inicia ciclo fresco (Pending de nuevo)
        var results = search.Search("https://example.com", 10);
        Assert.Single(results); // Pending de nuevo
    }

    [Fact]
    public async Task Search_AfterDnsResolved_HasOpenActionAndNoErrorTag() {
        var search = BuildSearch();
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;

        var results = search.Search("https://example.com", 10);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.NotEmpty(r.Actions);
        Assert.StartsWith("Open in", r.Subtitle);
        Assert.Null(r.ErrorTag);
    }
}

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
    [InlineData("hello world",              "",                           false)]  // tiene espacios
    [InlineData("hello",                    "",                           false)]  // sin punto
    [InlineData("report.pdf",              "",                            false)]  // TLD desconocido
    [InlineData("example.xyz",             "",                            false)]  // TLD desconocido
    [InlineData("",                         "",                           false)]
    [InlineData("abc",                      "",                           false)]
    [InlineData("/usr/local/bin",           "",                           false)]  // ruta local
    public void TryNormalizeUrl_CorrectlyClassifies(
        string query, string expectedUrl, bool expectedResult) {
        var result = UrlSearch.TryNormalizeUrl(query, out var url);
        Assert.Equal(expectedResult, result);
        if (expectedResult) Assert.Equal(expectedUrl, url);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UrlSearch BuildSearch(HttpStatusCode headStatusCode = HttpStatusCode.OK) {
        var handler = new FakeHttpMessageHandler(headStatusCode);
        var httpClient = new HttpClient(handler);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        return new UrlSearch(httpClient, settings, browserDiscovery, appIconCache, NullLogger<UrlSearch>.Instance);
    }

    /// <summary>Espera a que ResultChanged se dispare (máx <paramref name="timeoutMs"/> ms).</summary>
    private static Task WaitForResultChangedAsync(UrlSearch search, int timeoutMs = 3000) {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        search.ResultChanged += () => tcs.TrySetResult();
        return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
    }

    // ── Search behavior ───────────────────────────────────────────────────────

    [Fact]
    public void Search_NonUrl_ReturnsEmpty() {
        var search = BuildSearch();
        Assert.Empty(search.Search("hello world", 10));
        Assert.Empty(search.Search("just text", 10));
        Assert.Empty(search.Search("/local/path", 10));
    }

    [Fact]
    public void Search_UrlLooking_ReturnsPendingResultImmediately() {
        var search = BuildSearch();
        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
        var r = Assert.IsType<ResultItemViewModel>(results[0]);
        Assert.Equal("https://example.com", r.Title);
        Assert.Equal("Web", r.Category);
        Assert.Equal(3.0, r.Score);
        Assert.True(r.BypassLimit);
        Assert.NotNull(r.OnActivate);
    }

    [Fact]
    public async Task Search_AfterHead200_StillReturnsResult() {
        var search = BuildSearch(HttpStatusCode.OK);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        var results = search.Search("https://example.com", 10);
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_AfterHead404_StillReturnsResult() {
        // 404 means server responded — URL is reachable in a browser
        var search = BuildSearch(HttpStatusCode.NotFound);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Single(search.Search("https://example.com", 10));
    }

    [Fact]
    public async Task Search_AfterHead403_StillReturnsResult() {
        // 403 is common for sites that block HEAD (e.g. Amazon) — URL is still reachable
        var search = BuildSearch(HttpStatusCode.Forbidden);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Single(search.Search("https://example.com", 10));
    }

    [Fact]
    public async Task Search_AfterHead405_StillReturnsResult() {
        // 405 Method Not Allowed — server alive, HEAD blocked, still valid
        var search = BuildSearch(HttpStatusCode.MethodNotAllowed);
        var waitTask = WaitForResultChangedAsync(search);
        _ = search.Search("https://example.com", 10);
        await waitTask;
        Assert.Single(search.Search("https://example.com", 10));
    }

    [Fact]
    public void Search_SameUrlTwice_DoesNotStartTwoBackgroundChecks() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        var appIconCache = new AppIconCache(platform, NullLogger<AppIconCache>.Instance);
        var browserDiscovery = new BrowserDiscovery(settings, platform, NullLogger<BrowserDiscovery>.Instance);
        var search = new UrlSearch(httpClient, settings, browserDiscovery, appIconCache, NullLogger<UrlSearch>.Instance);

        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);
        _ = search.Search("https://example.com", 10);

        // Solo 1 background check por URL (HEAD + favicon = 2 llamadas máx, no 6)
        Thread.Sleep(50);
        Assert.True(handler.CallCount <= 2, $"Expected ≤2 calls, got {handler.CallCount}");
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
}

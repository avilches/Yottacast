using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Url;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

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
}

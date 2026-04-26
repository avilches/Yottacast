using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Dictionary;

namespace Yottacast.Core.Tests.Search;

public class DictionaryApiTests {
    private sealed class CapturingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(responseJson) });
        }
    }

    [Fact]
    public async Task LookupAsync_UsesCorrectDomain_ForSpanish() {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound);
        await DictionaryApiClient.LookupAsync(new HttpClient(handler), "casa", "es", NullLogger.Instance, CancellationToken.None);
        Assert.Contains("es.wiktionary.org", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task LookupAsync_UsesCorrectDomain_ForEnglish() {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound);
        await DictionaryApiClient.LookupAsync(new HttpClient(handler), "house", "en", NullLogger.Instance, CancellationToken.None);
        Assert.Contains("en.wiktionary.org", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_On404() {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound);
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "xyz", "es", NullLogger.Instance, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsOnlyRequestedLang() {
        var json = """
            {
              "es": [{"partOfSpeech":"Noun","language":"Spanish","definitions":[{"definition":"<p>Edificio.</p>"}]}],
              "en": [{"partOfSpeech":"Noun","language":"English","definitions":[{"definition":"<p>House.</p>"}]}]
            }
            """;
        var handler = new CapturingHandler(json);
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "casa", "es", NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Spanish", result[0].Language);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenLangNotInResponse() {
        var json = """{"en": [{"partOfSpeech":"Noun","language":"English","definitions":[{"definition":"<p>Hi.</p>"}]}]}""";
        var handler = new CapturingHandler(json);
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "casa", "es", NullLogger.Instance, CancellationToken.None);

        Assert.Null(result);
    }
}

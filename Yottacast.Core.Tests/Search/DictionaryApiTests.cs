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
    public async Task LookupAsync_CallsEnWiktionary() {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound);
        await DictionaryApiClient.LookupAsync(new HttpClient(handler), "casa", NullLogger.Instance, CancellationToken.None);
        Assert.Contains("en.wiktionary.org", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_On404() {
        var handler = new CapturingHandler("{}", HttpStatusCode.NotFound);
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "xyz", NullLogger.Instance, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsAllLanguages() {
        var json = """
            {
              "es": [{"partOfSpeech":"Noun","language":"Spanish","definitions":[{"definition":"<p>House.</p>"}]}],
              "en": [{"partOfSpeech":"Noun","language":"English","definitions":[{"definition":"<p>A house.</p>"}]}]
            }
            """;
        var handler = new CapturingHandler(json);
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "casa", NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.ContainsKey("es"));
        Assert.True(result.ContainsKey("en"));
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_OnEmptyJson() {
        var handler = new CapturingHandler("{}");
        var result = await DictionaryApiClient.LookupAsync(new HttpClient(handler), "xyz", NullLogger.Instance, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

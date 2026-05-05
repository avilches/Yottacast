using System.Net;
using System.Net.Http;

namespace Yottacast.Core.Tests.Fakes;

/// <summary>
/// HttpMessageHandler configurable para tests. Responde con el status code indicado
/// sin realizar ninguna petición de red real.
/// </summary>
internal class FakeHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler {
    public int CallCount { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}

using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

public sealed class FaviconCache {
    private readonly HttpClient _httpClient;
    private readonly ILogger<FaviconCache> _logger;

    public FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger)
        : this(httpClient, logger, AppPaths.FaviconCacheDir) { }

    internal FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger, string cacheDir) {
        _httpClient = httpClient;
        _logger = logger;
    }

    public event Action? FaviconLoaded;
    public byte[]? GetOrLoad(string host) => throw new NotImplementedException();
    public Task Stop() => throw new NotImplementedException();
}
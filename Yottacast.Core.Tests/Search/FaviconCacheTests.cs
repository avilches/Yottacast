using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;

namespace Yottacast.Core.Tests.Search;

public class FaviconCacheTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public void Dispose() {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private FaviconCache Build(HttpStatusCode code = HttpStatusCode.OK, byte[]? responseBytes = null) {
        var handler = new FakeHttpMessageHandler(code, responseBytes ?? [0x89, 0x50, 0x4E, 0x47]); // PNG header
        return new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);
    }

    [Fact]
    public async Task GetOrLoad_DiskHit_DoesNotFetch() {
        // Arrange: pre-populate disk cache
        Directory.CreateDirectory(_tempDir);
        var host = "example.com";
        File.WriteAllBytes(Path.Combine(_tempDir, $"{host}.png"), [0x89, 0x50, 0x4E, 0x47]);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);

        // Act
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();
        cache.GetOrLoad(host);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert: served from disk, no HTTP call
        Assert.Equal(0, handler.CallCount);
        Assert.NotNull(cache.GetOrLoad(host));
    }

    [Fact]
    public async Task GetOrLoad_DiskMiss_FetchesAndWritesToDisk() {
        var host = "example.com";
        var cache = Build();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();

        cache.GetOrLoad(host);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Favicon written to disk
        var file = Path.Combine(_tempDir, $"{host}.png");
        Assert.True(File.Exists(file));
        Assert.NotEmpty(File.ReadAllBytes(file));
    }

    [Fact]
    public async Task GetOrLoad_SameHostTwice_OnlyOneFetch() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, [0x89, 0x50]);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();

        cache.GetOrLoad("example.com");
        cache.GetOrLoad("example.com");
        cache.GetOrLoad("example.com");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FaviconLoaded_FiredAfterLoad() {
        var cache = Build();
        var fired = false;
        cache.FaviconLoaded += () => fired = true;

        cache.GetOrLoad("example.com");
        await Task.Delay(500);

        Assert.True(fired);
    }

    [Fact]
    public async Task GetOrLoad_HttpFailure_MarksNull_NoRetry() {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var cache = new FaviconCache(new HttpClient(handler), NullLogger<FaviconCache>.Instance, _tempDir);

        cache.GetOrLoad("example.com");
        await Task.Delay(500);

        // Second call must NOT start another HTTP request
        cache.GetOrLoad("example.com");
        await Task.Delay(200);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Stop_ClearsMemory() {
        var cache = Build();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.FaviconLoaded += () => tcs.TrySetResult();
        cache.GetOrLoad("example.com");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cache.Stop();

        // After Stop, memory is cleared (disk still exists)
        Assert.Null(cache.GetOrLoad("example.com")); // returns null until async reload
    }
}
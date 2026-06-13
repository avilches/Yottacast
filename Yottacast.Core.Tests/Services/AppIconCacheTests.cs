using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class AppIconCacheTests : IDisposable {
    private readonly string _appDir = Path.Combine(Path.GetTempPath(), "yc-app-" + Guid.NewGuid());
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "yc-iconcache-" + Guid.NewGuid());

    public AppIconCacheTests() {
        Directory.CreateDirectory(_appDir);
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose() {
        if (Directory.Exists(_appDir)) Directory.Delete(_appDir, recursive: true);
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private AppIconCache Build(PlatformProvider platform) =>
        new(platform, NullLogger<AppIconCache>.Instance, _cacheDir);

    /// <summary>Counts GetAppIconBytes calls; each call blocks on a gate to keep the dedup window open.</summary>
    private sealed class CountingPlatform : PlatformProvider {
        public int IconCalls;
        private readonly ManualResetEventSlim _gate = new(true);
        private readonly byte[] _bytes;

        public CountingPlatform(byte[]? bytes = null) => _bytes = bytes ?? [1, 2, 3, 4];

        public void Block() => _gate.Reset();
        public void Release() => _gate.Set();

        public override byte[]? GetAppIconBytes(string appPath) {
            Interlocked.Increment(ref IconCalls);
            _gate.Wait();
            return _bytes;
        }

        public override bool? IsSystemDarkMode() => null;
        public override List<string> DefaultAppDirectories() => [];
        public override List<string> DefaultSearchFolders() => [];
        public override Task SearchFilesAsync(string q, Action<FileResult> r, int m, IReadOnlyList<string>? f, CancellationToken c) => Task.CompletedTask;
        public override Task ScanAppsAsync(Action<string> a, IReadOnlyList<string> d, CancellationToken c) => Task.CompletedTask;
        public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(IReadOnlyList<string> d, Action<string> a, Action<string> r) => [];
        public override void LaunchApp(string path) { }
        public override string[] KnownBrowserNames => [];
        public override void OpenUrl(string url, string browserName) { }
        public override string[] KnownTerminalNames => [];
        public override void ExecuteCommand(string command, string terminalName) { }
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 5000) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(10);
        Assert.True(condition(), "condition not met within timeout");
    }

    [Fact]
    public async Task PreloadAsync_ConcurrentCalls_OnlyOnePlatformCall() {
        var platform = new CountingPlatform();
        var cache = Build(platform);
        platform.Block(); // hold the first load inside the platform call so the dedup window stays open

        for (var i = 0; i < 20; i++) cache.PreloadAsync(_appDir);
        await WaitFor(() => platform.IconCalls >= 1);
        platform.Release();
        await WaitFor(() => cache.Get(_appDir) != null);

        Assert.Equal(1, platform.IconCalls);
    }

    [Fact]
    public async Task PreloadAsync_AfterLoaded_DoesNotReload() {
        var platform = new CountingPlatform();
        var cache = Build(platform);

        cache.PreloadAsync(_appDir);
        await WaitFor(() => cache.Get(_appDir) != null);
        cache.PreloadAsync(_appDir);
        cache.PreloadAsync(_appDir);
        await Task.Delay(100);

        Assert.Equal(1, platform.IconCalls);
    }

    [Fact]
    public async Task LoadFromPlatform_DeletesOrphansForSameApp() {
        var platform = new CountingPlatform();
        var cache = Build(platform);

        // Pre-seed an orphan PNG with the same app hash but an old mtime/version suffix.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(_appDir)));
        var orphan = Path.Combine(_cacheDir, $"{hash}_111111_v1.png");
        File.WriteAllBytes(orphan, [9, 9]);

        cache.PreloadAsync(_appDir);
        await WaitFor(() => cache.Get(_appDir) != null);

        Assert.False(File.Exists(orphan), "orphan from an older mtime/version should be deleted");
        var remaining = Directory.GetFiles(_cacheDir, $"{hash}_*.png");
        Assert.Single(remaining);
    }
}

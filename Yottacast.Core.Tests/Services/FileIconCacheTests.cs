using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Platform;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class FileIconCacheTests : IDisposable {
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "yc-fileicons-" + Guid.NewGuid());

    public FileIconCacheTests() => Directory.CreateDirectory(_cacheDir);

    public void Dispose() {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
    }

    private FileIconCache Build(PlatformProvider platform) =>
        new(platform, NullLogger<FileIconCache>.Instance, _cacheDir);

    private sealed class CountingPlatform : PlatformProvider {
        public int FileIconCalls;
        private readonly byte[]? _bytes;
        public CountingPlatform(byte[]? bytes) => _bytes = bytes;

        public override byte[]? GetFileIconBytes(string filePath) {
            Interlocked.Increment(ref FileIconCalls);
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
    public async Task GetOrPreload_LoadFailure_AllowsRetry() {
        // Platform throws -> Load catches, leaves key unset, clears the in-flight guard.
        var platform = new ThrowingPlatform();
        var cache = Build(platform);

        Assert.Null(cache.GetOrPreload("/tmp/a.weirdext"));
        await WaitFor(() => platform.Calls >= 1);
        await Task.Delay(50);

        // A second request must be able to retry (key not poisoned, guard not stuck).
        Assert.Null(cache.GetOrPreload("/tmp/a.weirdext"));
        await WaitFor(() => platform.Calls >= 2);
        Assert.True(platform.Calls >= 2, "load should be retried after a transient failure");
    }

    private sealed class ThrowingPlatform : CountingThrowBase {
        public int Calls;
        public override byte[]? GetFileIconBytes(string filePath) {
            Interlocked.Increment(ref Calls);
            throw new IOException("boom");
        }
    }

    private abstract class CountingThrowBase : PlatformProvider {
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

    [Fact]
    public void InvalidateAll_DeletesAllVersionsNotJustCurrent() {
        // Seed icons from the current version AND an older one.
        File.WriteAllBytes(Path.Combine(_cacheDir, "java_v1.png"), [1]);
        File.WriteAllBytes(Path.Combine(_cacheDir, "pdf_v1.png"), [1]);
        File.WriteAllBytes(Path.Combine(_cacheDir, "java_v0.png"), [1]); // orphan from an older CacheVersion
        var cache = Build(new CountingPlatform([1, 2]));

        cache.InvalidateAll();

        Assert.Empty(Directory.GetFiles(_cacheDir, "*.png"));
    }

    [Fact]
    public async Task GetOrPreload_DiskHit_DoesNotCallPlatform() {
        File.WriteAllBytes(Path.Combine(_cacheDir, "pdf_v1.png"), [7, 7, 7]);
        var platform = new CountingPlatform([1, 2]);
        var cache = Build(platform);

        var bytes = cache.GetOrPreload("/tmp/report.pdf");

        Assert.NotNull(bytes);
        await Task.Delay(50);
        Assert.Equal(0, platform.FileIconCalls);
    }
}

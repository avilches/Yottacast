using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

/// <summary>
/// Two-level cache for domain favicons: memory (instant) and disk (persists across launches).
/// Disk layout: {FaviconCacheDir}/{host}.png
/// </summary>
public sealed class FaviconCache {
    private readonly HttpClient _httpClient;
    private readonly ILogger<FaviconCache> _logger;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();
    private readonly ConcurrentDictionary<string, bool> _started = new();

    public FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger)
        : this(httpClient, logger, AppPaths.FaviconCacheDir) { }

    internal FaviconCache(HttpClient httpClient, ILogger<FaviconCache> logger, string cacheDir) {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDir = cacheDir;
    }

    /// <summary>Fires on a thread-pool thread when a favicon finishes loading with non-null bytes.</summary>
    public event Action? FaviconLoaded;

    /// <summary>
    /// Returns cached bytes if available, triggers async load on first call per host.
    /// Returns null while loading or if favicon could not be obtained.
    /// On a transient IO error (disk read/write) the host is not marked null and its started-guard
    /// is cleared, so a later call retries instead of being stuck forever.
    /// </summary>
    public byte[]? GetOrLoad(string host) {
        if (_memory.TryGetValue(host, out var cached)) return cached;
        if (_started.TryAdd(host, true))
            _ = Task.Run(() => LoadAsync(host));
        return null;
    }

    /// <summary>Clears in-memory cache and dedup state. Disk cache persists across sessions.</summary>
    public Task Stop() {
        _memory.Clear();
        _started.Clear();
        return Task.CompletedTask;
    }

    private async Task LoadAsync(string host) {
        var diskPath = Path.Combine(_cacheDir, $"{host}.png");
        try {
            // Phase 1: disk cache
            if (File.Exists(diskPath)) {
                var diskBytes = File.ReadAllBytes(diskPath);
                if (diskBytes.Length > 0) {
                    _memory[host] = diskBytes;
                    _logger.LogDebug("FaviconCache: disk hit for {Host} ({N} bytes)", host, diskBytes.Length);
                    FaviconLoaded?.Invoke();
                    return;
                }
            }

            // Phase 2: HTTP fetch from Google favicon service
            var url = $"https://www.google.com/s2/favicons?sz=64&domain={host}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppDefaults.FaviconTimeoutSeconds));
            var bytes = await _httpClient.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
            if (bytes.Length > 0) {
                _memory[host] = bytes;
                Directory.CreateDirectory(_cacheDir);
                File.WriteAllBytes(diskPath, bytes);
                _logger.LogDebug("FaviconCache: fetched and cached {Host} ({N} bytes)", host, bytes.Length);
                FaviconLoaded?.Invoke();
            } else {
                _memory[host] = null;
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException) {
            // A favicon could genuinely not be obtained: mark as null so we do not retry on every keystroke.
            _memory[host] = null;
            _logger.LogDebug("FaviconCache: fetch failed for {Host}: {Message}", host, ex.Message);
        } catch (Exception ex) {
            // Transient IO error (e.g. disk read/write). Clear the started guard so a later call can retry,
            // and leave the host unmarked so it is not stuck null forever.
            _started.TryRemove(host, out _);
            _logger.LogDebug(ex, "FaviconCache: load error for {Host}, will retry on next request", host);
        }
    }
}
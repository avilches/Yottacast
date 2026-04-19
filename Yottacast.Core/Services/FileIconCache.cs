using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

/// <summary>
/// Two-level cache for file type icons: memory (instant) and disk (persists across launches).
/// Keyed by file extension — all files of the same type share one icon entry.
///
/// Disk layout: ~/.cache/yottacast/file-icons/{ext}.png  (e.g. "java.png", "pdf.png")
/// No mtime needed: icon is type-based, not file-specific.
///
/// Call Get/GetOrPreload with a file path; the extension is extracted internally.
/// </summary>
public sealed class FileIconCache(PlatformProvider platform, ILogger<FileIconCache> logger) {
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();
    private readonly string _cacheDir = AppPaths.FileIconCacheDir;

    /// <summary>Fired (on a thread-pool thread) when an icon finishes loading with non-null bytes.</summary>
    public event Action? IconLoaded;

    /// <summary>Returns cached icon bytes for the file's type, or null if not yet loaded.</summary>
    public byte[]? Get(string filePath) => _memory.GetValueOrDefault(ExtKey(filePath));

    /// <summary>
    /// Returns icon bytes immediately if available in memory or disk cache (keyed by extension).
    /// If not cached, queues an async background load via the platform (slow path).
    /// Safe to call multiple times — only the first miss per extension triggers a load.
    /// </summary>
    public byte[]? GetOrPreload(string filePath) {
        var key = ExtKey(filePath);
        if (_memory.TryGetValue(key, out var cached)) return cached;
        var bytes = TryDiskCache(key);
        if (bytes != null) {
            _memory[key] = bytes;
            return bytes;
        }
        if (_loading.TryAdd(key, 0))
            _ = Task.Run(() => Load(filePath, key));
        return null;
    }

    private void Load(string filePath, string key) {
        if (_memory.ContainsKey(key)) return;
        try {
            var bytes = LoadFromPlatform(filePath, key);
            _memory[key] = bytes;
            if (bytes is not null) {
                logger.LogDebug("File icon ready ({Bytes} bytes): {Ext}", bytes.Length, key);
                IconLoaded?.Invoke();
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "File icon load failed: {Ext}", key);
            _memory[key] = null;
        }
    }

    private byte[]? TryDiskCache(string key) {
        var file = DiskCachePath(key);
        if (!File.Exists(file)) return null;
        var bytes = File.ReadAllBytes(file);
        logger.LogDebug("File icon disk-cache hit ({Bytes} bytes): {Ext}", bytes.Length, key);
        return bytes;
    }

    private byte[]? LoadFromPlatform(string filePath, string key) {
        var bytes = platform.GetFileIconBytes(filePath);
        if (bytes is null) return null;
        Directory.CreateDirectory(_cacheDir);
        File.WriteAllBytes(DiskCachePath(key), bytes);
        return bytes;
    }

    /// <summary>
    /// Clears the entire memory cache and deletes all disk-cached icons for the current version.
    /// Use when a full rebuild is required (e.g. CacheVersion bump during migration).
    /// </summary>
    public void InvalidateAll() {
        _memory.Clear();
        _loading.Clear();
        if (!Directory.Exists(_cacheDir)) return;
        foreach (var f in Directory.GetFiles(_cacheDir, $"*_{CacheVersion}.png")) {
            try { File.Delete(f); } catch { /* best-effort */ }
        }
        logger.LogInformation("File icon cache fully invalidated");
    }

    private const string CacheVersion = "v1";

    private string DiskCachePath(string key) =>
        Path.Combine(_cacheDir, $"{key}_{CacheVersion}.png");

    /// <summary>Normalised extension key: lowercase without dot, or "_none" for files with no extension.</summary>
    private static string ExtKey(string filePath) {
        var ext = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(ext) ? "_none" : ext.TrimStart('.').ToLowerInvariant();
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

/// <summary>
/// Two-level cache for app icons: memory (instant) and disk (persists across launches).
///
/// Disk layout: ~/.cache/yottacast/app-icons/{sha1(path)}_{mtime_unix}.png
/// The mtime suffix invalidates the entry automatically when the app updates.
///
/// Call PreloadAsync after an app is discovered. Search() reads from memory via Get().
/// </summary>
public sealed class AppIconCache(PlatformProvider platform, ILogger<AppIconCache> logger) {
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();
    private readonly string _cacheDir = AppPaths.AppIconCacheDir;

    /// <summary>Fired (on a thread-pool thread) when an icon finishes loading with non-null bytes.</summary>
    public event Action? IconLoaded;

    /// <summary>Returns cached icon bytes for the given app path, or null if not yet loaded.</summary>
    public byte[]? Get(string appPath) => _memory.GetValueOrDefault(appPath);

    /// <summary>
    /// Loads the icon for appPath into the memory cache (non-blocking, runs on a thread-pool thread).
    /// Checks the disk cache first; calls the platform only on a miss.
    /// Safe to call multiple times for the same path — only the first call does work.
    /// </summary>
    public void PreloadAsync(string appPath) {
        if (_memory.ContainsKey(appPath)) return;
        _ = Task.Run(() => Load(appPath));
    }

    /// <summary>
    /// Invalidates the memory cache entry and re-queues the icon for loading.
    /// Used when an app bundle is updated after initial detection (e.g. still being copied).
    /// </summary>
    public void Reload(string appPath) {
        _memory.TryRemove(appPath, out _);
        _ = Task.Run(() => Load(appPath));
    }

    private void Load(string appPath) {
        // Guard against duplicate concurrent loads for the same path
        if (_memory.ContainsKey(appPath)) return;

        var name = Path.GetFileNameWithoutExtension(appPath);
        logger.LogDebug("Icon load start: {App}", name);
        try {
            var bytes = TryDiskCache(appPath) ?? LoadFromPlatform(appPath);
            _memory[appPath] = bytes;
            if (bytes is null)
                logger.LogDebug("Icon not available (null): {App}", name);
            else {
                logger.LogDebug("Icon ready ({Bytes} bytes): {App}", bytes.Length, name);
                IconLoaded?.Invoke();
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Icon load failed: {App}", name);
            _memory[appPath] = null;
        }
    }

    private byte[]? TryDiskCache(string appPath) {
        var file = DiskCachePath(appPath);
        if (file is null || !File.Exists(file)) return null;
        var bytes = File.ReadAllBytes(file);
        logger.LogDebug("Icon disk-cache hit ({Bytes} bytes): {App}",
            bytes.Length, Path.GetFileNameWithoutExtension(appPath));
        return bytes;
    }

    private byte[]? LoadFromPlatform(string appPath) {
        logger.LogDebug("Icon calling platform.GetAppIconBytes: {App}",
            Path.GetFileNameWithoutExtension(appPath));
        var bytes = platform.GetAppIconBytes(appPath);
        if (bytes is null) {
            logger.LogDebug("Icon platform returned null: {App}",
                Path.GetFileNameWithoutExtension(appPath));
            return null;
        }

        logger.LogDebug("Icon platform returned {Bytes} bytes: {App}",
            bytes.Length, Path.GetFileNameWithoutExtension(appPath));
        var file = DiskCachePath(appPath);
        if (file is not null) {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllBytes(file, bytes);
        }
        return bytes;
    }

    /// <summary>Returns the disk cache file path for this app, or null if the path cannot be determined.</summary>
    private string? DiskCachePath(string appPath) {
        try {
            var mtime = new DateTimeOffset(Directory.GetLastWriteTimeUtc(appPath)).ToUnixTimeSeconds();
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(appPath)));
            return Path.Combine(_cacheDir, $"{hash}_{mtime}_v2.png");
        } catch {
            return null;
        }
    }
}

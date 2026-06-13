using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

/// <summary>
/// Two-level cache for app icons: memory (instant) and disk (persists across launches).
///
/// Disk layout: ~/.cache/yottacast/app-icons/{sha1(path)}_{mtime_unix}_v2.png
/// The mtime suffix invalidates the entry automatically when the app updates; when a fresh icon
/// is written, stale files for the same app (older mtime or cache version) are deleted so the
/// disk cache stays bounded.
///
/// Call PreloadAsync after an app is discovered. Search() reads from memory via Get().
/// Concurrent PreloadAsync/Reload calls for the same path are deduplicated via _loading,
/// so N simultaneous callers trigger only one platform call.
/// </summary>
public sealed class AppIconCache {
    private readonly PlatformProvider _platform;
    private readonly ILogger<AppIconCache> _logger;
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();
    private readonly string _cacheDir;

    public AppIconCache(PlatformProvider platform, ILogger<AppIconCache> logger)
        : this(platform, logger, AppPaths.AppIconCacheDir) { }

    internal AppIconCache(PlatformProvider platform, ILogger<AppIconCache> logger, string cacheDir) {
        _platform = platform;
        _logger = logger;
        _cacheDir = cacheDir;
    }

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
        if (!_loading.TryAdd(appPath, 0)) return;
        _ = Task.Run(() => Load(appPath));
    }

    /// <summary>
    /// Invalidates the memory cache entry and re-queues the icon for loading.
    /// Used when an app bundle is updated after initial detection (e.g. still being copied).
    /// If a load for this path is already in flight, that load picks up the change (it always
    /// re-reads from disk/platform), so Reload does not queue a duplicate.
    /// </summary>
    public void Reload(string appPath) {
        _memory.TryRemove(appPath, out _);
        if (!_loading.TryAdd(appPath, 0)) return;
        _ = Task.Run(() => Load(appPath));
    }

    private void Load(string appPath) {
        // Guard against duplicate concurrent loads for the same path
        if (_memory.ContainsKey(appPath)) {
            _loading.TryRemove(appPath, out _);
            return;
        }

        var name = Path.GetFileNameWithoutExtension(appPath);
        _logger.LogDebug("Icon load start: {App}", name);
        try {
            var bytes = TryDiskCache(appPath) ?? LoadFromPlatform(appPath);
            _memory[appPath] = bytes;
            if (bytes is null)
                _logger.LogDebug("Icon not available (null): {App}", name);
            else {
                _logger.LogDebug("Icon ready ({Bytes} bytes): {App}", bytes.Length, name);
                IconLoaded?.Invoke();
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Icon load failed: {App}", name);
            _memory[appPath] = null;
        } finally {
            _loading.TryRemove(appPath, out _);
        }
    }

    private byte[]? TryDiskCache(string appPath) {
        var file = DiskCachePath(appPath);
        if (file is null || !File.Exists(file)) return null;
        var bytes = File.ReadAllBytes(file);
        _logger.LogDebug("Icon disk-cache hit ({Bytes} bytes): {App}",
            bytes.Length, Path.GetFileNameWithoutExtension(appPath));
        return bytes;
    }

    private byte[]? LoadFromPlatform(string appPath) {
        _logger.LogDebug("Icon calling platform.GetAppIconBytes: {App}",
            Path.GetFileNameWithoutExtension(appPath));
        var bytes = _platform.GetAppIconBytes(appPath);
        if (bytes is null) {
            _logger.LogDebug("Icon platform returned null: {App}",
                Path.GetFileNameWithoutExtension(appPath));
            return null;
        }

        _logger.LogDebug("Icon platform returned {Bytes} bytes: {App}",
            bytes.Length, Path.GetFileNameWithoutExtension(appPath));
        var file = DiskCachePath(appPath);
        if (file is not null) {
            Directory.CreateDirectory(_cacheDir);
            DeleteOrphans(appPath, file);
            File.WriteAllBytes(file, bytes);
        }
        return bytes;
    }

    /// <summary>
    /// Deletes stale disk-cache entries for this app left over from previous mtimes or cache versions.
    /// Disk filenames are {sha1(path)}_{mtime}_{version}.png; only the current file (keepFile) is kept,
    /// so entries from older app versions or bumped cache versions never accumulate.
    /// </summary>
    private void DeleteOrphans(string appPath, string keepFile) {
        try {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(appPath)));
            foreach (var f in Directory.GetFiles(_cacheDir, $"{hash}_*.png")) {
                if (string.Equals(f, keepFile, StringComparison.Ordinal)) continue;
                try { File.Delete(f); } catch { /* best-effort */ }
            }
        } catch {
            /* best-effort cleanup; never block icon loading */
        }
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

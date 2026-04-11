using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;

namespace Yottacast.Core.Services;

/// <summary>
/// In-memory cache for file icons loaded via the platform (NSWorkspace on macOS).
/// Call PreloadAsync when a file is discovered; call Get on each snapshot to assign IconBytes.
/// </summary>
public sealed class FileIconCache(PlatformProvider platform, ILogger<FileIconCache> logger) {
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new();

    /// <summary>Returns cached icon bytes for the given file path, or null if not yet loaded.</summary>
    public byte[]? Get(string filePath) => _memory.GetValueOrDefault(filePath);

    /// <summary>
    /// Triggers a background load of the icon for filePath.
    /// Safe to call multiple times for the same path — only the first call does work.
    /// </summary>
    public void PreloadAsync(string filePath) {
        if (_memory.ContainsKey(filePath)) return;
        _ = Task.Run(() => Load(filePath));
    }

    private void Load(string filePath) {
        if (_memory.ContainsKey(filePath)) return;
        try {
            var bytes = platform.GetFileIconBytes(filePath);
            _memory[filePath] = bytes;
            if (bytes is not null)
                logger.LogDebug("File icon ready ({Bytes} bytes): {File}", bytes.Length, Path.GetFileName(filePath));
        } catch (Exception ex) {
            logger.LogWarning(ex, "File icon load failed: {File}", Path.GetFileName(filePath));
            _memory[filePath] = null;
        }
    }
}

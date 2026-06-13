using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

/// <summary>
/// Bridge that lets Core code read/write the clipboard without depending on Avalonia.
/// The GUI project calls Initialize() once at startup with Avalonia-backed implementations.
/// </summary>
public class ClipboardService(ILogger<ClipboardService> logger)
{
    private Action<string>? _copy;
    private Func<Task<string?>>? _read;

    public void Initialize(Action<string> copy, Func<Task<string?>> read)
    {
        _copy = copy;
        _read = read;
    }

    public void CopyText(string text) {
        if (_copy is null) {
            logger.LogWarning("CopyText llamado antes de Initialize; ignorado.");
            return;
        }
        _copy.Invoke(text);
    }

    public Task<string?> ReadTextAsync() {
        if (_read is null) {
            logger.LogWarning("ReadTextAsync llamado antes de Initialize; devuelve null.");
            return Task.FromResult<string?>(null);
        }
        return _read.Invoke();
    }
}

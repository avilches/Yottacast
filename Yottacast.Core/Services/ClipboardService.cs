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

    /// <summary>The last text passed to CopyText, or null if none yet. Useful in tests.</summary>
    public string? LastCopied { get; private set; }

    public void Initialize(Action<string> copy, Func<Task<string?>> read)
    {
        _copy = copy;
        _read = read;
    }

    public void CopyText(string text) {
        LastCopied = text;
        _copy?.Invoke(text);
    }

    public Task<string?> ReadTextAsync() =>
        _read?.Invoke() ?? Task.FromResult<string?>(null);
}

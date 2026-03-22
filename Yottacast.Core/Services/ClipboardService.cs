using Microsoft.Extensions.Logging;

namespace Yottacast.Core.Services;

/// <summary>
/// Bridge that lets Core code request a clipboard write without depending on Avalonia.
/// The GUI project calls Initialize() once at startup with an Avalonia-backed implementation.
/// </summary>
public class ClipboardService(ILogger<ClipboardService> logger) {
    private Action<string>? _copy;

    public void Initialize(Action<string> copy) => _copy = copy;

    public void CopyText(string text) => _copy?.Invoke(text);
}

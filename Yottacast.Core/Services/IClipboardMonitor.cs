namespace Yottacast.Core.Services;

public interface IClipboardMonitor {
    event Action<string> TextCopied;
    void Start();
    Task Stop();
}

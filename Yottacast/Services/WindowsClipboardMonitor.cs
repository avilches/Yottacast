using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;

namespace Yottacast.Services;

/// <summary>
/// Polls the Windows clipboard every 500ms via OpenClipboard/GetClipboardData/CloseClipboard.
/// When the text content changes, fires TextCopied.
/// </summary>
public sealed class WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor> logger) : IClipboardMonitor, IDisposable
{
    private CancellationTokenSource? _cts;
    private string? _lastText;

    public event Action<string>? TextCopied;

    public void Start()
    {
        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _cts, cts);
        prev?.Cancel();
        prev?.Dispose();
        _ = PollAsync(cts.Token);
    }

    public Task Stop()
    {
        var prev = Interlocked.Exchange(ref _cts, null);
        prev?.Cancel();
        prev?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => Stop();

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AppDefaults.ClipboardMonitorIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var text = ReadText();
                if (text is not null && text != _lastText)
                {
                    _lastText = text;
                    TextCopied?.Invoke(text);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogWarning("WindowsClipboardMonitor: poll error: {Message}", ex.Message);
        }
    }

    private string? ReadText()
    {
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero) return null;
                var ptr = GlobalLock(hData);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(hData); }
            }
            finally { CloseClipboard(); }
        }
        catch (Exception ex)
        {
            logger.LogDebug("WindowsClipboardMonitor: ReadText failed: {Message}", ex.Message);
            return null;
        }
    }

    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}

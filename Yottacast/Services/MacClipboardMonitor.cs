using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yottacast.Core;
using Yottacast.Core.Services;

namespace Yottacast.Services;

/// <summary>
/// Polls NSPasteboard.generalPasteboard every 500ms. When changeCount changes,
/// reads plain text content and fires TextCopied. Polling stops when Stop() is called.
/// </summary>
public sealed class MacClipboardMonitor(ILogger<MacClipboardMonitor> logger) : IClipboardMonitor, IDisposable
{
    private CancellationTokenSource? _cts;
    private int _lastChangeCount = -1;

    public event Action<string>? TextCopied;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollAsync(_cts.Token);
    }

    public Task Stop()
    {
        _cts?.Cancel();
        _cts = null;
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
                if (text is not null)
                    TextCopied?.Invoke(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogWarning("MacClipboardMonitor: poll error: {Message}", ex.Message);
        }
    }

    private string? ReadText()
    {
        try
        {
            var pb = ObjcMsgSend(ObjcGetClass("NSPasteboard"), SelRegisterName("generalPasteboard"));
            if (pb == IntPtr.Zero) return null;

            var count = ObjcMsgSendInt(pb, SelRegisterName("changeCount"));
            if (count == _lastChangeCount) return null;
            _lastChangeCount = count;

            var nsStringAlloc = ObjcMsgSend(ObjcGetClass("NSString"), SelRegisterName("alloc"));
            var typeStr = ObjcMsgSendInitString(nsStringAlloc, SelRegisterName("initWithUTF8String:"),
                "public.utf8-plain-text");
            var strObj = ObjcMsgSendArg(pb, SelRegisterName("stringForType:"), typeStr);
            ObjcRelease(typeStr);
            if (strObj == IntPtr.Zero) return null;

            var utf8Ptr = ObjcMsgSend(strObj, SelRegisterName("UTF8String"));
            if (utf8Ptr == IntPtr.Zero) return null;

            return Marshal.PtrToStringUTF8(utf8Ptr);
        }
        catch (Exception ex)
        {
            logger.LogDebug("MacClipboardMonitor: ReadText failed: {Message}", ex.Message);
            return null;
        }
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr sel);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern int ObjcMsgSendInt(IntPtr receiver, IntPtr sel);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendInitString(IntPtr receiver, IntPtr sel,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendArg(IntPtr receiver, IntPtr sel, IntPtr arg);

    [DllImport("libobjc.dylib", EntryPoint = "objc_release")]
    private static extern void ObjcRelease(IntPtr obj);
}

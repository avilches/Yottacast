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

    private static readonly IntPtr SelGeneralPasteboard  = SelRegisterName("generalPasteboard");
    private static readonly IntPtr SelChangeCount        = SelRegisterName("changeCount");
    private static readonly IntPtr SelAlloc              = SelRegisterName("alloc");
    private static readonly IntPtr SelInitWithUtf8String = SelRegisterName("initWithUTF8String:");
    private static readonly IntPtr SelStringForType      = SelRegisterName("stringForType:");
    private static readonly IntPtr SelUtf8String         = SelRegisterName("UTF8String");
    private static readonly IntPtr ClsNSPasteboard       = ObjcGetClass("NSPasteboard");
    private static readonly IntPtr ClsNSString           = ObjcGetClass("NSString");

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
            var pb = ObjcMsgSend(ClsNSPasteboard, SelGeneralPasteboard);
            if (pb == IntPtr.Zero) return null;

            var count = ObjcMsgSendInt(pb, SelChangeCount);
            if (count == _lastChangeCount) return null;
            _lastChangeCount = count;

            var nsStringAlloc = ObjcMsgSend(ClsNSString, SelAlloc);
            var typeStr = ObjcMsgSendInitString(nsStringAlloc, SelInitWithUtf8String,
                "public.utf8-plain-text");
            var strObj = ObjcMsgSendArg(pb, SelStringForType, typeStr);
            ObjcRelease(typeStr);
            if (strObj == IntPtr.Zero) return null;

            var utf8Ptr = ObjcMsgSend(strObj, SelUtf8String);
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

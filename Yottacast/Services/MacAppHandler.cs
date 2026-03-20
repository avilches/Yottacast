using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Yottacast.Services;

internal sealed class MacAppHandler : AppHandler {
    private IntPtr _previousApp = IntPtr.Zero;

    // NSApplicationActivationPolicyAccessory = 1: no Dock icon, no menu bar
    public override void OnFrameworkInitializationCompleted() {
        var nsApp = ObjcMsgSend(ObjcGetClass("NSApplication"), SelRegisterName("sharedApplication"));
        ObjcMsgSendPolicy(nsApp, SelRegisterName("setActivationPolicy:"), 1);
    }

    // Captures the current frontmost app and activates Yottacast.
    public override void OnShow() {
        if (_previousApp != IntPtr.Zero) ObjcRelease(_previousApp);
        _previousApp = ObjcRetain(GetFrontmostApp());
        var nsApp = ObjcMsgSend(ObjcGetClass("NSApplication"), SelRegisterName("sharedApplication"));
        ObjcMsgSendActivate(nsApp, SelRegisterName("activateIgnoringOtherApps:"), 1);
    }

    // Restores focus to the app that was frontmost before Yottacast was shown.
    public override void OnHide() {
        if (_previousApp == IntPtr.Zero) return;
        // NSApplicationActivateIgnoringOtherApps = 2
        ObjcMsgSendActivate(_previousApp, SelRegisterName("activateWithOptions:"), 2);
        ObjcRelease(_previousApp);
        _previousApp = IntPtr.Zero;
    }

    public override (KeyModifiers Modifiers, Key Key) CloseWindowShortcut => (KeyModifiers.Meta, Key.W);

    private static IntPtr GetFrontmostApp() {
        var workspace = ObjcMsgSend(ObjcGetClass("NSWorkspace"), SelRegisterName("sharedWorkspace"));
        return ObjcMsgSend(workspace, SelRegisterName("frontmostApplication"));
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendPolicy(IntPtr receiver, IntPtr selector, long policy);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendActivate(IntPtr receiver, IntPtr selector, ulong options);

    [DllImport("libobjc.dylib", EntryPoint = "objc_retain")]
    private static extern IntPtr ObjcRetain(IntPtr obj);

    [DllImport("libobjc.dylib", EntryPoint = "objc_release")]
    private static extern void ObjcRelease(IntPtr obj);
}
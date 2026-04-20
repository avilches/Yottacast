using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Yottacast.Core;

namespace Yottacast.Services;

internal sealed class MacAppHandler : AppHandler {
    private IntPtr _previousApp = IntPtr.Zero;

    // NSApplicationActivationPolicyAccessory = 1: no Dock icon, no menu bar shown.
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
    public override (KeyModifiers Modifiers, Key Key)? QuitShortcut => (KeyModifiers.Meta, Key.Q);

    public override string CtrlSymbol  => "⌃";
    public override string AltSymbol   => "⌥";
    public override string ShiftSymbol => "⇧";
    public override string MetaSymbol  => "⌘";

    public override IReadOnlyList<(KeyModifiers, Key)> ForbiddenHotkeys =>
        [(KeyModifiers.Meta, Key.Q), (KeyModifiers.Meta, Key.W)];

    public override void HideCursor() =>
        ObjcMsgSendBool(ObjcGetClass("NSCursor"), SelRegisterName("setHiddenUntilMouseMoves:"), true);

    public override void ShowCursor() =>
        ObjcMsgSendBool(ObjcGetClass("NSCursor"), SelRegisterName("setHiddenUntilMouseMoves:"), false);

    // CGEvent coordinate space: origin at upper-left of primary display, Y increasing downward.
    // This matches Avalonia's PixelPoint coordinate space directly, no Y-flip needed.
    public override PixelPoint? GetMousePosition() {
        var event_ = CGEventCreate(IntPtr.Zero);
        if (event_ == IntPtr.Zero) return null;
        var loc = CGEventGetLocation(event_);
        CFRelease(event_);
        return new PixelPoint((int)Math.Round(loc.X), (int)Math.Round(loc.Y));
    }

    // Waits for the previous app to take focus, then posts a Cmd+V keyboard event via CGEvent.
    // kCGHIDEventTap=0, kCGEventFlagMaskCommand=0x100000, keyCode for 'v'=0x09
    public override async Task SimulatePasteAsync() {
        await Task.Delay(AppDefaults.PasteDelayMs);
        var vDown = CGEventCreateKeyboardEvent(IntPtr.Zero, 0x09, true);
        CGEventSetFlags(vDown, 0x100000);
        var vUp = CGEventCreateKeyboardEvent(IntPtr.Zero, 0x09, false);
        CGEventSetFlags(vUp, 0x100000);
        CGEventPost(0, vDown);
        CGEventPost(0, vUp);
        CFRelease(vDown);
        CFRelease(vUp);
    }

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

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendObject(IntPtr receiver, IntPtr selector, IntPtr obj);
    [DllImport("libobjc.dylib", EntryPoint = "objc_retain")]
    private static extern IntPtr ObjcRetain(IntPtr obj);

    [DllImport("libobjc.dylib", EntryPoint = "objc_release")]
    private static extern void ObjcRelease(IntPtr obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint {
        public double X;
        public double Y;
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CgPoint CGEventGetLocation(IntPtr event_);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr event_, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tap, IntPtr event_);

    // macOS Settings theme — NSColor equivalents for Light and Dark.
    // Font: SF Pro Text (system font on macOS, resolved via CoreText).
    public override void ApplySettingsTheme(Window window) {
        window.FontFamily = new FontFamily("SF Pro Text, Helvetica Neue");
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = MakeThemeDict(
            ("Theme.WindowBackground",   Brush("#F5F5F5")),  // windowBackgroundColor
            ("Theme.ItemIconBackground", Brush("#E8E8E8")),  // sidebar
            ("Theme.Divider",            Brush("#D1D1D6")),  // separatorColor
            ("Theme.ItemTitle",          Brush("#1C1C1E")),  // labelColor
            ("Theme.ItemSubtitle",       Brush("#636366")),  // secondaryLabelColor
            ("Theme.ItemCategory",       Brush("#8E8E93")),  // tertiaryLabelColor
            ("Theme.ItemSelection",      Brush("#007AFF")),  // systemBlueColor
            ("Theme.ItemSelectionText",  Brush("#FFFFFF")),
            ("Theme.ItemHover",          Brush("#14000000")),
            ("Theme.FooterText",         Brush("#8E8E93")),
            ("Theme.SearchCaret",        Brush("#007AFF")),
            ("Theme.FontSizeTitle",      13d),
            ("Theme.FontSizeSmall",      11d),
            ("Theme.FontSizeNoResults",  14d)
        );
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = MakeThemeDict(
            ("Theme.WindowBackground",   Brush("#282828")),  // windowBackgroundColor dark
            ("Theme.ItemIconBackground", Brush("#1E1E1E")),  // sidebar dark
            ("Theme.Divider",            Brush("#3A3A3C")),  // separatorColor dark
            ("Theme.ItemTitle",          Brush("#FFFFFF")),  // labelColor dark
            ("Theme.ItemSubtitle",       Brush("#ABABAB")),  // secondaryLabelColor dark
            ("Theme.ItemCategory",       Brush("#6C6C70")),  // tertiaryLabelColor dark
            ("Theme.ItemSelection",      Brush("#0A84FF")),  // systemBlueColor dark
            ("Theme.ItemSelectionText",  Brush("#FFFFFF")),
            ("Theme.ItemHover",          Brush("#1AFFFFFF")),
            ("Theme.FooterText",         Brush("#6C6C70")),
            ("Theme.SearchCaret",        Brush("#0A84FF")),
            ("Theme.FontSizeTitle",      13d),
            ("Theme.FontSizeSmall",      11d),
            ("Theme.FontSizeNoResults",  14d)
        );
    }

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}
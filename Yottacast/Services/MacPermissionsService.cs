using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Yottacast.Services;

internal sealed class MacPermissionsService : PermissionsService {
    public override bool IsSupported => true;

    public override IReadOnlyList<PermissionId> Available { get; } =
        [PermissionId.Accessibility, PermissionId.FullDiskAccess];

    public override PermissionInfo Check(PermissionId id) => id switch {
        PermissionId.Accessibility   => CheckAccessibility(),
        PermissionId.FullDiskAccess  => CheckFullDiskAccess(),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public override void Request(PermissionId id) {
        switch (id) {
            case PermissionId.Accessibility:
                // Native prompt only fires the first time the app asks. After that the
                // call returns silently; the panel fallback covers re-grant scenarios.
                TryAccessibilityPrompt();
                OpenSystemSettings("com.apple.preference.security?Privacy_Accessibility");
                break;
            case PermissionId.FullDiskAccess:
                OpenSystemSettings("com.apple.preference.security?Privacy_AllFiles");
                break;
        }
    }

    // ── Accessibility ────────────────────────────────────────────────────────

    private static PermissionInfo CheckAccessibility() {
        var granted = AXIsProcessTrusted();
        return new PermissionInfo(
            PermissionId.Accessibility,
            "Accessibility",
            "Required for the global hotkey and for simulating Cmd+V after picking an emoji. Restart Yottacast after granting it.",
            granted ? PermissionStatus.Granted : PermissionStatus.Denied);
    }

    private static void TryAccessibilityPrompt() {
        var key = ResolveCFRef("kAXTrustedCheckOptionPrompt");
        var value = ResolveCFRef("kCFBooleanTrue");
        var keyCB = dlsym(RtldDefault, "kCFTypeDictionaryKeyCallBacks");
        var valCB = dlsym(RtldDefault, "kCFTypeDictionaryValueCallBacks");
        if (key == IntPtr.Zero || value == IntPtr.Zero || keyCB == IntPtr.Zero || valCB == IntPtr.Zero) {
            // Cannot build the options dict — fall back to opening Settings only.
            return;
        }

        var dict = CFDictionaryCreateMutable(IntPtr.Zero, 0, keyCB, valCB);
        if (dict == IntPtr.Zero) return;
        try {
            CFDictionaryAddValue(dict, key, value);
            AXIsProcessTrustedWithOptions(dict);
        } finally {
            CFRelease(dict);
        }
    }

    // ── Full Disk Access ─────────────────────────────────────────────────────

    private static PermissionInfo CheckFullDiskAccess() {
        // TCC.db lives at this path with restrictive perms (root:wheel 0600).
        // Apps with Full Disk Access can read it; apps without get EACCES.
        // It always exists on a working macOS install.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tccDb = Path.Combine(home, "Library", "Application Support", "com.apple.TCC", "TCC.db");

        var status = TryRead(tccDb);
        if (status == PermissionStatus.Unknown) {
            // Secondary heuristic in case TCC.db moved or doesn't exist.
            var bookmarks = Path.Combine(home, "Library", "Safari", "Bookmarks.plist");
            status = TryRead(bookmarks);
        }

        return new PermissionInfo(
            PermissionId.FullDiskAccess,
            "Full Disk Access",
            "Lets file search cover system-protected directories (Mail, Messages, Safari…).",
            status);
    }

    private static PermissionStatus TryRead(string path) {
        try {
            using var stream = File.OpenRead(path);
            stream.ReadByte();
            return PermissionStatus.Granted;
        } catch (UnauthorizedAccessException) {
            return PermissionStatus.Denied;
        } catch (FileNotFoundException) {
            return PermissionStatus.Unknown;
        } catch (DirectoryNotFoundException) {
            return PermissionStatus.Unknown;
        } catch (IOException) {
            // Includes EACCES surfaced as IOException on some runtimes.
            return PermissionStatus.Denied;
        }
    }

    // ── System Settings panel launcher ───────────────────────────────────────

    private static void OpenSystemSettings(string panelId) {
        try {
            Process.Start(new ProcessStartInfo {
                FileName = "open",
                Arguments = $"\"x-apple.systempreferences:{panelId}\"",
                UseShellExecute = false,
            });
        } catch {
            // Best-effort: if launching System Settings fails, the user still sees
            // the red dot and can open Settings manually.
        }
    }

    // ── CoreFoundation / dlsym helpers ───────────────────────────────────────

    private static readonly IntPtr RtldDefault = new(-2);

    private static IntPtr ResolveCFRef(string symbol) {
        var ptr = dlsym(RtldDefault, symbol);
        return ptr == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(ptr);
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryCreateMutable(
        IntPtr allocator, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFDictionaryAddValue(IntPtr dict, IntPtr key, IntPtr value);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}

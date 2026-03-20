using System.Runtime.InteropServices;

namespace Yottacast.Core.Platform;

/// <summary>
/// P/Invoke wrapper around the macOS CoreServices MDQuery (Spotlight) API.
/// All public methods are synchronous and block the calling thread; wrap in Task.Run when needed.
/// </summary>
internal static class SpotlightInterop {
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreServices =
        "/System/Library/Frameworks/CoreServices.framework/CoreServices";

    private const uint KCFStringEncodingUtf8 = 0x08000100;
    private const uint KMDQuerySynchronous = 1;

    // kCFTypeArrayCallBacks is an exported global variable in CoreFoundation.
    private static readonly IntPtr KCFTypeArrayCallBacks;

    static SpotlightInterop() {
        var lib = NativeLibrary.Load(CoreFoundation);
        KCFTypeArrayCallBacks = NativeLibrary.GetExport(lib, "kCFTypeArrayCallBacks");
    }

    // ── CoreFoundation ────────────────────────────────────────────────────────

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator, string cStr, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayCreate(
        IntPtr allocator, IntPtr[] values, long numValues, IntPtr callBacks);

    [DllImport(CoreFoundation)]
    private static extern bool CFStringGetCString(
        IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    // ── CoreServices MDQuery ──────────────────────────────────────────────────

    [DllImport(CoreServices)]
    private static extern IntPtr MDQueryCreate(
        IntPtr allocator, IntPtr queryString,
        IntPtr valueListAttrs, IntPtr sortingAttrs);

    [DllImport(CoreServices)]
    private static extern void MDQuerySetSearchScope(
        IntPtr query, IntPtr scopeDirectories, uint scopeOptions);

    [DllImport(CoreServices)]
    private static extern bool MDQueryExecute(IntPtr query, uint optionFlags);

    [DllImport(CoreServices)]
    private static extern long MDQueryGetResultCount(IntPtr query);

    [DllImport(CoreServices)]
    private static extern IntPtr MDQueryGetResultAtIndex(IntPtr query, long idx);

    [DllImport(CoreServices)]
    private static extern IntPtr MDItemCopyAttribute(IntPtr item, IntPtr attrName);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a synchronous Spotlight query and delivers file paths via <paramref name="onLine"/>.
    /// Return <c>false</c> from <paramref name="onLine"/> to stop iteration early.
    /// </summary>
    public static void Query(
        string predicate,
        IReadOnlyList<string>? scopes,
        Func<string, bool> onLine,
        CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var owned = new List<IntPtr>();
        var pathBuffer = new byte[4096];

        try {
            var predicateCf = CFStringCreateWithCString(IntPtr.Zero, predicate, KCFStringEncodingUtf8);
            owned.Add(predicateCf);

            var query = MDQueryCreate(IntPtr.Zero, predicateCf, IntPtr.Zero, IntPtr.Zero);
            owned.Add(query);

            if (scopes is { Count: > 0 }) {
                var scopeRefs = scopes
                    .Select(s => CFStringCreateWithCString(IntPtr.Zero, s, KCFStringEncodingUtf8))
                    .ToArray();
                owned.AddRange(scopeRefs);

                var scopeArray = CFArrayCreate(IntPtr.Zero, scopeRefs, scopeRefs.Length, KCFTypeArrayCallBacks);
                owned.Add(scopeArray);

                MDQuerySetSearchScope(query, scopeArray, 0);
            }

            MDQueryExecute(query, KMDQuerySynchronous);
            ct.ThrowIfCancellationRequested();

            var attrName = CFStringCreateWithCString(IntPtr.Zero, "kMDItemPath", KCFStringEncodingUtf8);
            owned.Add(attrName);

            var count = MDQueryGetResultCount(query);
            for (long i = 0; i < count; i++) {
                ct.ThrowIfCancellationRequested();

                // MDQueryGetResultAtIndex does NOT transfer ownership — no release needed.
                var item = MDQueryGetResultAtIndex(query, i);
                var pathCf = MDItemCopyAttribute(item, attrName); // Copy → we own it
                if (pathCf == IntPtr.Zero) continue;

                try {
                    if (!CFStringGetCString(pathCf, pathBuffer, pathBuffer.Length, KCFStringEncodingUtf8))
                        continue;
                    var nullIdx = Array.IndexOf(pathBuffer, (byte)0);
                    var path = System.Text.Encoding.UTF8.GetString(pathBuffer, 0, nullIdx < 0 ? pathBuffer.Length : nullIdx);
                    if (!onLine(path)) break;
                } finally {
                    CFRelease(pathCf);
                }
            }
        } finally {
            foreach (var ptr in owned)
                if (ptr != IntPtr.Zero) CFRelease(ptr);
        }
    }
}
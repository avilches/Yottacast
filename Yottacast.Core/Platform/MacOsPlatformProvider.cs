using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.UserDocuments;
using Yottacast.Core.Services;

namespace Yottacast.Core.Platform;

public sealed class MacOsPlatformProvider(ILogger<MacOsPlatformProvider> logger) : PlatformProvider {
    // ── Dark mode ─────────────────────────────────────────────────────────────

    public override bool? IsSystemDarkMode() {
        try {
            using var p = System.Diagnostics.Process.Start(new ProcessStartInfo(
                "defaults", "read -g AppleInterfaceStyle") {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && output.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        } catch {
            return null;
        }
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    public override List<string> DefaultAppDirectories() => ["/Applications", "$HOME/Applications", "/System/Applications", "/System/Applications/Utilities"];

    public override List<string> DefaultSearchFolders() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [
            "$HOME/Downloads",
            "$HOME/Desktop",
            "$HOME/Documents",
            "$HOME/Movies",
            "$HOME/Pictures",
            "$HOME/Dropbox",
            "$HOME/Music",
            "$HOME/Public",
            "$HOME/Library/Mobile Documents/com~apple~CloudDocs",
            "$HOME/Library/Application Support",
            "$HOME/Library/Containers",
            "$HOME/Creative Cloud Files",
            "$HOME/Google Drive",
            "$HOME/OneDrive",
            "$HOME/Box Sync",
            "$HOME/Box",
            "$HOME/Mega",
            "$HOME/Cloud Drive",
            "$HOME/Nextcloud",
            "$HOME/Adobe Creative Cloud",
            "$HOME/Amazon Drive"
        ];
    }

    // ── App scanning ──────────────────────────────────────────────────────────

    public override Task ScanAppsAsync(
        Action<string> addApp, IReadOnlyList<string> dirs, CancellationToken ct) {
        if (dirs.Count == 0) return Task.CompletedTask;
        const string predicate = "kMDItemContentType == 'com.apple.application-bundle'";
        return Task.Run(() => SpotlightInterop.Query(
            predicate, dirs,
            line => {
                if (!string.IsNullOrWhiteSpace(line)) addApp(line);
                return true;
            },
            ct), ct);
    }

    public override IReadOnlyList<FileSystemWatcher> CreateAppWatchers(
        IReadOnlyList<string> dirs, Action<string> onAdded, Action<string> onRemoved) {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs.Where(Directory.Exists)) {
            var watcher = new FileSystemWatcher(dir) {
                Filter = "*.app",
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => onAdded(e.FullPath);
            watcher.Changed += (_, e) => onAdded(e.FullPath);
            watcher.Deleted += (_, e) => onRemoved(e.FullPath);
            watchers.Add(watcher);
        }
        return watchers;
    }

    public override void LaunchApp(string path) {
        try {
            Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false });
        } catch {
        }
    }

    public override void RevealInFileManager(string directoryPath) {
        try {
            Process.Start(new ProcessStartInfo("open", $"\"{directoryPath}\"") { UseShellExecute = false });
        } catch {
        }
    }

    public override void OpenFile(string filePath) {
        try {
            Process.Start(new ProcessStartInfo("open", $"\"{filePath}\"") { UseShellExecute = false });
        } catch {
        }
    }

    // ── File search ───────────────────────────────────────────────────────────

    public override async Task SearchFilesAsync(
        string query, Action<FileResult> onResult, int maxResults,
        IReadOnlyList<string>? folders, CancellationToken ct) {
        var safeQuery = query.Replace("'", "\\'");
        if (string.IsNullOrEmpty(safeQuery)) return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var validFolders = folders?.Where(Directory.Exists).ToList();
        var invalidFolders = folders?.Where(f => !Directory.Exists(f)).ToList();
        if (invalidFolders?.Count > 0)
            logger.LogWarning("Spotlight skipping non-existent folders: [{Folders}]", string.Join(", ", invalidFolders));
        var scope = (validFolders?.Count > 0 ? validFolders : null) ?? [home];

        string predicate;
        if (safeQuery.Contains('*')) {
            predicate = $"kMDItemFSName == '{safeQuery}'cd";
        } else {
            var tokens = safeQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            predicate = string.Join(" && ", tokens.Select(t => $"kMDItemFSName == '*{t}*'cd"));
        }
        logger.LogDebug("Spotlight query: {Predicate} scope=[{Scope}]", predicate, string.Join(", ", scope));

        var count = 0;
        var sw = Stopwatch.StartNew();
        Exception? error = null;
        try {
            await Task.Run(() => SpotlightInterop.Query(
                predicate, scope,
                line => {
                    onResult(new FileResult(Path.GetFileName(line), line));
                    return ++count < maxResults;
                },
                ct), ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            error = ex;
        }
        sw.Stop();
        logger.LogDebug("Spotlight result: elapsed={ElapsedMs}ms cancelled={Cancelled} results={Count} error={Error}",
            sw.Elapsed.TotalMilliseconds, ct.IsCancellationRequested, count, error?.Message ?? "none");
    }

    public override string? AppPathInDirectory(string dir, string appName) => $"{dir}/{appName}.app";

    // ── Browser ───────────────────────────────────────────────────────────────

    public override string[] KnownBrowserNames => [
        "Safari",
        "Google Chrome",
        "Firefox",
        "Brave Browser",
        "Microsoft Edge",
        "Opera",
        "Arc",
        "Vivaldi",
        "Chromium",
        "Tor Browser",
        "DuckDuckGo",
        "Orion",
    ];

    public override void OpenUrl(string url, string browserName) {
        try {
            Process.Start(new ProcessStartInfo {
                FileName = "open",
                ArgumentList = { "-a", browserName, url },
                UseShellExecute = false,
            });
        } catch {
        }
    }

    // ── Terminal ──────────────────────────────────────────────────────────────

    public override string[] KnownTerminalNames => [
        "Terminal",
        "iTerm",
        "Warp",
        "Alacritty",
        "Kitty",
        "Hyper",
        "WezTerm",
        "Tabby",
    ];

    public override void ExecuteCommand(string command, string terminalName) {
        switch (terminalName) {
            case "Terminal":
                RunAppleScript($"""tell application "Terminal" to do script "{EscapeAppleScript(command)}" """);
                break;
            case "iTerm":
                RunAppleScript($"""
                                tell application "iTerm"
                                    create window with default profile command "{EscapeAppleScript(command)}"
                                end tell
                                """);
                break;
            case "Warp":
                var warpUrl = $"warp://action/new_tab?command={Uri.EscapeDataString(command)}";
                System.Diagnostics.Process.Start(new ProcessStartInfo {
                    FileName = "open",
                    ArgumentList = { warpUrl },
                    UseShellExecute = false,
                });
                break;
            default:
                var script = Path.GetTempFileName() + ".command";
                File.WriteAllText(script, $"#!/bin/sh\n{command}\n");
                System.Diagnostics.Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();
                System.Diagnostics.Process.Start(new ProcessStartInfo {
                    FileName = "open",
                    ArgumentList = { "-a", terminalName, script },
                    UseShellExecute = false,
                });
                break;
        }
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the file's icon IS provided by the same app bundle as the default app —
    /// meaning the badge would show the same logo that's already visible in the large icon.
    /// Detection: check if the app's Info.plist registers a custom document-type icon for the
    /// file's extension (CFBundleDocumentTypes entry with CFBundleTypeIconFile + matching extension).
    /// </summary>
    public override bool AreIconsSame(string filePath, string appPath) {
        try {
            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return false;

            var infoPlistPath = Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(infoPlistPath)) return false;

            var cfPlistPath = CfStringCreateWithCString(IntPtr.Zero, infoPlistPath, 0x08000100);
            if (cfPlistPath == IntPtr.Zero) return false;
            try {
                // NSDictionary handles both XML and binary plist formats
                var plist = ObjcMsgSendArg(ObjcGetClass("NSDictionary"),
                    SelRegisterName("dictionaryWithContentsOfFile:"), cfPlistPath);
                if (plist == IntPtr.Zero) return false;

                var docTypesKey = CfStringCreateWithCString(IntPtr.Zero, "CFBundleDocumentTypes", 0x08000100);
                var iconFileKey = CfStringCreateWithCString(IntPtr.Zero, "CFBundleTypeIconFile", 0x08000100);
                var extListKey = CfStringCreateWithCString(IntPtr.Zero, "CFBundleTypeExtensions", 0x08000100);
                var cfExt = CfStringCreateWithCString(IntPtr.Zero, ext, 0x08000100);
                try {
                    var docTypes = ObjcMsgSendArg(plist, SelRegisterName("objectForKey:"), docTypesKey);
                    if (docTypes == IntPtr.Zero) return false;

                    var count = (int)ObjcMsgSendNint(docTypes, SelRegisterName("count"));
                    for (var i = 0; i < count; i++) {
                        var entry = ObjcMsgSendArgNint(docTypes, SelRegisterName("objectAtIndex:"), i);
                        if (entry == IntPtr.Zero) continue;

                        // Only suppress badge when the app registered its OWN icon for this type
                        var iconFile = ObjcMsgSendArg(entry, SelRegisterName("objectForKey:"), iconFileKey);
                        if (iconFile == IntPtr.Zero) continue;

                        var exts = ObjcMsgSendArg(entry, SelRegisterName("objectForKey:"), extListKey);
                        if (exts == IntPtr.Zero) continue;

                        if (ObjcMsgSendArgByte(exts, SelRegisterName("containsObject:"), cfExt) != 0) {
                            logger.LogDebug("AreIconsSame [{File}] [{App}] → true (.{Ext} has CFBundleTypeIconFile)",
                                Path.GetFileName(filePath), Path.GetFileName(appPath), ext);
                            return true;
                        }
                    }
                } finally {
                    CfRelease(docTypesKey);
                    CfRelease(iconFileKey);
                    CfRelease(extListKey);
                    CfRelease(cfExt);
                }
            } finally {
                CfRelease(cfPlistPath);
            }
            return false;
        } catch (Exception ex) {
            logger.LogWarning(ex, "AreIconsSame exception");
            return false;
        }
    }

    private double PixelMeanAbsDiff(IntPtr tiff1, IntPtr tiff2) {
        var rep1 = ObjcMsgSendArg(ObjcGetClass("NSBitmapImageRep"), SelRegisterName("imageRepWithData:"), tiff1);
        var rep2 = ObjcMsgSendArg(ObjcGetClass("NSBitmapImageRep"), SelRegisterName("imageRepWithData:"), tiff2);
        if (rep1 == IntPtr.Zero || rep2 == IntPtr.Zero) return double.MaxValue;

        var bpr = (int)ObjcMsgSendNint(rep1, SelRegisterName("bytesPerRow"));
        var h = (int)ObjcMsgSendNint(rep1, SelRegisterName("pixelsHigh"));
        var bpr2 = (int)ObjcMsgSendNint(rep2, SelRegisterName("bytesPerRow"));
        var h2 = (int)ObjcMsgSendNint(rep2, SelRegisterName("pixelsHigh"));
        if (bpr != bpr2 || h != h2) return double.MaxValue;

        var total = bpr * h;
        var ptr1 = ObjcMsgSend(rep1, SelRegisterName("bitmapData"));
        var ptr2 = ObjcMsgSend(rep2, SelRegisterName("bitmapData"));
        if (ptr1 == IntPtr.Zero || ptr2 == IntPtr.Zero) return double.MaxValue;

        var b1 = new byte[total];
        var b2 = new byte[total];
        Marshal.Copy(ptr1, b1, 0, total);
        Marshal.Copy(ptr2, b2, 0, total);

        long diff = 0;
        for (var i = 0; i < total; i++)
            diff += Math.Abs(b1[i] - b2[i]);
        return (double)diff / total;
    }

    private static IntPtr RenderToTiff(IntPtr nsImage, int size) {
        var alloc = ObjcMsgSend(ObjcGetClass("NSImage"), SelRegisterName("alloc"));
        if (alloc == IntPtr.Zero) return IntPtr.Zero;
        var small = ObjcMsgSendSizeReturn(alloc, SelRegisterName("initWithSize:"),
            new NSSize { Width = size, Height = size });
        if (small == IntPtr.Zero) return IntPtr.Zero;
        ObjcMsgSend(small, SelRegisterName("lockFocus"));
        // Composite onto white so alpha differences don't affect pixel comparison
        var white = ObjcMsgSend(ObjcGetClass("NSColor"), SelRegisterName("whiteColor"));
        ObjcMsgSend(white, SelRegisterName("setFill"));
        NSRectFill(new NSRect { X = 0, Y = 0, Width = size, Height = size });
        ObjcMsgSendRectVoid(nsImage, SelRegisterName("drawInRect:"),
            new NSRect { X = 0, Y = 0, Width = size, Height = size });
        ObjcMsgSend(small, SelRegisterName("unlockFocus"));
        var tiff = ObjcMsgSend(small, SelRegisterName("TIFFRepresentation"));
        CfRelease(small);
        return tiff;
    }

    public override string? GetDefaultAppPath(string filePath) {
        try {
            var cfPath = CfStringCreateWithCString(IntPtr.Zero, filePath, 0x08000100);
            if (cfPath == IntPtr.Zero) return null;
            try {
                var fileUrl = ObjcMsgSendArg(ObjcGetClass("NSURL"), SelRegisterName("fileURLWithPath:"), cfPath);
                if (fileUrl == IntPtr.Zero) return null;

                var workspace = ObjcMsgSend(ObjcGetClass("NSWorkspace"), SelRegisterName("sharedWorkspace"));
                if (workspace == IntPtr.Zero) return null;

                var appUrl = ObjcMsgSendArg(workspace, SelRegisterName("URLForApplicationToOpenURL:"), fileUrl);
                if (appUrl == IntPtr.Zero) return null;

                var pathStr = ObjcMsgSend(appUrl, SelRegisterName("path"));
                if (pathStr == IntPtr.Zero) return null;

                var utf8Ptr = ObjcMsgSend(pathStr, SelRegisterName("UTF8String"));
                if (utf8Ptr == IntPtr.Zero) return null;

                return Marshal.PtrToStringUTF8(utf8Ptr);
            } finally {
                CfRelease(cfPath);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "GetDefaultAppPath [{File}]: exception", filePath);
            return null;
        }
    }

    public override byte[]? GetFileIconBytes(string filePath) => GetAppIconBytes(filePath);

    public override byte[]? GetAppIconBytes(string appPath) {
        var name = Path.GetFileNameWithoutExtension(appPath);
        try {
            var workspace = ObjcMsgSend(ObjcGetClass("NSWorkspace"), SelRegisterName("sharedWorkspace"));
            if (workspace == IntPtr.Zero) {
                logger.LogWarning("Icon [{App}]: NSWorkspace.sharedWorkspace returned zero", name);
                return null;
            }

            var cfPath = CfStringCreateWithCString(IntPtr.Zero, appPath, 0x08000100);
            if (cfPath == IntPtr.Zero) {
                logger.LogWarning("Icon [{App}]: CFStringCreateWithCString returned zero", name);
                return null;
            }

            try {
                var nsImage = ObjcMsgSendArg(workspace, SelRegisterName("iconForFile:"), cfPath);
                if (nsImage == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: iconForFile: returned zero", name);
                    return null;
                }

                // Draw into a new NSImage at exact targetSize×targetSize points (lockFocus pattern from
                // Quicksilver's prepareImageForIcon:). AppKit picks the best available .icns representation.
                // On Retina (2×) produces 128×128 pixels — more than enough for 28×28 logical display.
                // Avoids the inconsistency of TIFFRepresentation returning all reps (16×16 to 1024×1024)
                // and imageRepWithData: picking any one of them unpredictably.
                const int targetSize = 64;
                var alloc = ObjcMsgSend(ObjcGetClass("NSImage"), SelRegisterName("alloc"));
                if (alloc == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: NSImage alloc returned zero", name);
                    return null;
                }

                var scaledImage = ObjcMsgSendSizeReturn(alloc, SelRegisterName("initWithSize:"),
                    new NSSize { Width = targetSize, Height = targetSize });
                if (scaledImage == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: NSImage initWithSize: returned zero", name);
                    return null;
                }

                ObjcMsgSend(scaledImage, SelRegisterName("lockFocus"));
                ObjcMsgSendRectVoid(nsImage, SelRegisterName("drawInRect:"),
                    new NSRect { X = 0, Y = 0, Width = targetSize, Height = targetSize });
                ObjcMsgSend(scaledImage, SelRegisterName("unlockFocus"));

                var tiffData = ObjcMsgSend(scaledImage, SelRegisterName("TIFFRepresentation"));
                CfRelease(scaledImage);
                if (tiffData == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: TIFFRepresentation returned zero", name);
                    return null;
                }

                var bitmapRep = ObjcMsgSendArg(ObjcGetClass("NSBitmapImageRep"),
                    SelRegisterName("imageRepWithData:"), tiffData);
                if (bitmapRep == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: imageRepWithData: returned zero", name);
                    return null;
                }

                var emptyDict = ObjcMsgSend(ObjcGetClass("NSDictionary"), SelRegisterName("dictionary"));
                // NSBitmapImageFileTypePNG = 4
                var pngData = ObjcMsgSendRepresentation(bitmapRep,
                    SelRegisterName("representationUsingType:properties:"), 4, emptyDict);
                if (pngData == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: representationUsingType:properties: returned zero", name);
                    return null;
                }

                var length = (int)ObjcMsgSendNint(pngData, SelRegisterName("length"));
                if (length <= 0) {
                    logger.LogWarning("Icon [{App}]: NSData.length={Length}", name, length);
                    return null;
                }

                var bytesPtr = ObjcMsgSend(pngData, SelRegisterName("bytes"));
                if (bytesPtr == IntPtr.Zero) {
                    logger.LogWarning("Icon [{App}]: NSData.bytes returned zero", name);
                    return null;
                }

                var bytes = new byte[length];
                Marshal.Copy(bytesPtr, bytes, 0, length);
                logger.LogDebug("Icon [{App}]: OK {Bytes} bytes", name, bytes.Length);
                return bytes;
            } finally {
                CfRelease(cfPath);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Icon [{App}]: exception", name);
            return null;
        }
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendArg(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendRepresentation(IntPtr receiver, IntPtr selector, int fileType, IntPtr props);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint ObjcMsgSendNint(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendSize(IntPtr receiver, IntPtr selector, NSSize size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendSizeReturn(IntPtr receiver, IntPtr selector, NSSize size);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendRectVoid(IntPtr receiver, IntPtr selector, NSRect rect);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte ObjcMsgSendArgByte(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendArgNint(IntPtr receiver, IntPtr selector, nint index);

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSRectFill(NSRect rect);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CfStringCreateWithCString(IntPtr allocator, string str, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        EntryPoint = "CFRelease")]
    private static extern void CfRelease(IntPtr cf);

    // ── Running apps ──────────────────────────────────────────────────────────

    public override IReadOnlyList<RunningAppInfo> GetRunningApps() {
        try {
            var workspace = RaMsgSend(RaObjcGetClass("NSWorkspace"), RaSel("sharedWorkspace"));
            if (workspace == IntPtr.Zero) {
                logger.LogWarning("GetRunningApps: NSWorkspace.sharedWorkspace returned zero");
                return [];
            }
            var appsArray = RaMsgSend(workspace, RaSel("runningApplications"));
            if (appsArray == IntPtr.Zero) return [];

            // runningApplications returns an autoreleased array. Retain it so the
            // autorelease pool drain during AppKit text-input callbacks cannot
            // collect the array (and its NSRunningApplication entries) while we
            // iterate, which would produce a use-after-free "unrecognized selector"
            // ObjC exception that C# try/catch cannot intercept.
            RaMsgSend(appsArray, RaSel("retain"));
            try {
                var count      = (int)RaMsgSendCount(appsArray, RaSel("count"));
                var nsraClass  = RaObjcGetClass("NSRunningApplication");
                var selIsKind  = RaSel("isKindOfClass:");
                var selAtIndex = RaSel("objectAtIndex:");
                var selPid     = RaSel("processIdentifier");

                // proc_pidpath gives us the executable's real path (e.g.
                // /Applications/Safari.app/Contents/MacOS/Safari). We walk up the
                // path to find the .app bundle. This bypasses bundleURL/bundlePath
                // which silently return nil on the private NSRunningApplication
                // subclasses used in macOS 16 (Darwin 25).
                const uint maxPathLen = 4096;
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result    = new List<RunningAppInfo>(count);
                for (nuint i = 0; i < (nuint)count; i++) {
                    var app = RaMsgSendAtIndex(appsArray, selAtIndex, i);
                    if (app == IntPtr.Zero) continue;
                    if (RaMsgSendBoolSel(app, selIsKind, nsraClass) == IntPtr.Zero) continue;
                    var pid = RaMsgSendPid(app, selPid);
                    var buf = new byte[maxPathLen];
                    var len = RaProcPidPath(pid, buf, maxPathLen);
                    if (len <= 0) continue;
                    var exePath    = Encoding.UTF8.GetString(buf, 0, len);
                    var bundlePath = FindAppBundlePath(exePath);
                    if (bundlePath == null) continue;
                    bundlePath = NormalizeCryptexPath(bundlePath);
                    if (!seenPaths.Add(bundlePath)) continue;
                    result.Add(new RunningAppInfo(bundlePath, pid));
                }
                logger.LogDebug("GetRunningApps: found {Count} running apps, first 3: {Paths}",
                    result.Count,
                    string.Join(", ", result.Take(3).Select(r => r.Path)));
                return result;
            } finally {
                RaMsgSend(appsArray, RaSel("release"));
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "GetRunningApps failed");
            return [];
        }
    }

    private static string? FindAppBundlePath(string execPath) {
        // Search from the end to handle nested bundles (e.g. Simulator.app inside Xcode.app)
        var parts = execPath.Split('/');
        for (var i = parts.Length - 1; i >= 0; i--) {
            if (parts[i].EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return string.Join("/", parts.Take(i + 1));
        }
        return null;
    }

    // On macOS 16 (Darwin 25), some Apple apps live in the Cryptex volume.
    // proc_pidpath returns the real path (e.g. /System/Volumes/Preboot/Cryptexes/App/…/Safari.app)
    // while Spotlight returns the logical symlinked path (e.g. /Applications/Safari.app).
    // Translate the real Cryptex path back to the logical path so it matches the app cache.
    private static string NormalizeCryptexPath(string bundlePath) {
        const string cryptexPrefix = "/System/Volumes/Preboot/Cryptexes/App";
        if (!bundlePath.StartsWith(cryptexPrefix, StringComparison.Ordinal)) return bundlePath;
        var appName = Path.GetFileName(bundlePath);
        foreach (var dir in new[] { "/Applications", "/System/Applications", "/System/Applications/Utilities" }) {
            var candidate = $"{dir}/{appName}";
            if (Directory.Exists(candidate)) return candidate;
        }
        return bundlePath;
    }

    public override void QuitApp(int pid) {
        try { RaKill(pid, 15); } catch { }  // SIGTERM
    }

    public override void ForceQuitApp(int pid) {
        try { RaKill(pid, 9); } catch { }   // SIGKILL
    }

    // ── Dynamic settings ──────────────────────────────────────────────────────

    public override string? GetCurrentWifiNetworkName() {
        try {
            foreach (var iface in new[] { "en0", "en1" }) {
                using var p = Process.Start(new ProcessStartInfo {
                    FileName               = "networksetup",
                    Arguments              = $"-getairportnetwork {iface}",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                });
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                const string prefix = "Current Wi-Fi Network: ";
                if (output.StartsWith(prefix, StringComparison.Ordinal))
                    return output[prefix.Length..];
            }
            return null;
        } catch {
            return null;
        }
    }

    public override IReadOnlyList<string> GetActiveVpnNames() {
        try {
            using var p = Process.Start(new ProcessStartInfo {
                FileName               = "scutil",
                Arguments              = "--nc list",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            });
            if (p is null) return [];
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var names = new List<string>();
            // Format: "* (Connected)   UUID   Name   <Type>"
            foreach (var line in output.Split('\n')) {
                if (!line.Contains("(Connected)", StringComparison.Ordinal)) continue;
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"\(Connected\)\s+[\dA-Fa-f-]{36}\s+(.+?)\s+<");
                if (match.Success)
                    names.Add(match.Groups[1].Value.Trim().Trim('"'));
            }
            return names;
        } catch {
            return [];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RunAppleScript(string script) {
        System.Diagnostics.Process.Start(new ProcessStartInfo {
            FileName = "osascript",
            ArgumentList = { "-e", script },
            UseShellExecute = false,
        });
    }

    private static string EscapeAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Running apps P/Invokes ────────────────────────────────────────────────

    [DllImport("libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr RaObjcGetClass(string name);

    [DllImport("libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr RaSel(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr RaMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint RaMsgSendCount(IntPtr receiver, IntPtr selector);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr RaMsgSendAtIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern int RaMsgSendPid(IntPtr receiver, IntPtr selector);

    [DllImport("libproc", EntryPoint = "proc_pidpath")]
    private static extern int RaProcPidPath(int pid, byte[] buffer, uint bufferSize);

    // BOOL return on arm64: value lives in x0 as 0 or 1 — IntPtr reads it safely.
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr RaMsgSendBoolSel(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("libc", EntryPoint = "kill")]
    private static extern int RaKill(int pid, int sig);
}
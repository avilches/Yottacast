using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.UserDocuments;

/// <summary>
/// IDeferredSearchSource that searches user files via FileSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// Yields progressive snapshots as FileSearch emits results — each snapshot is the current
/// best-N sorted results seen so far. Snapshots are emitted every SnapshotEvery results
/// and once after mdfind finishes or the timeout triggers.
/// </summary>
public class UserDocumentSearch(
    UserSettings settings,
    FileSearch fileSearch,
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ILogger<UserDocumentSearch> logger,
    ClipboardService clipboard,
    int timeoutMs = AppDefaults.FileSearchTimeoutMs) : IDeferredSearchSource {

    // Badge icon cache: keyed by lowercase extension; null means "no default app found"
    private readonly ConcurrentDictionary<string, byte[]?> _badgeByExtension = new();
    // Default-app display name cache: keyed by lowercase extension; null means "no default app or
    // file IS the app". Decoupled from the badge cache because the badge can be suppressed
    // (same icon, e.g. .cs → Rider) while the app name is still useful for the "Open in X" label.
    private readonly ConcurrentDictionary<string, string?> _appNameByExtension = new();
    private readonly ConcurrentDictionary<string, byte> _badgePreloading = new();
    private readonly string _badgeCacheDir = AppPaths.BadgeIconCacheDir;
    private const string BadgeCacheVersion = "v1";

    /// <summary>Fired (on a thread-pool thread) when badge or app-name metadata finishes loading for an extension.</summary>
    public event Action? BadgeIconLoaded;

    /// <summary>Returns the cached badge icon bytes for the file's extension, or null if suppressed/not yet loaded.</summary>
    public byte[]? GetBadge(string filePath) {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? null : _badgeByExtension.GetValueOrDefault(ext);
    }

    /// <summary>Returns the cached default-app display name for the file's extension, or null if unknown/not yet resolved.</summary>
    public string? GetDefaultAppName(string filePath) {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? null : _appNameByExtension.GetValueOrDefault(ext);
    }

    /// <summary>Clears all badge/app-name caches (memory and disk). Called when installed apps change.</summary>
    public void InvalidateAll() {
        _badgeByExtension.Clear();
        _appNameByExtension.Clear();
        _badgePreloading.Clear();
        if (!Directory.Exists(_badgeCacheDir)) return;
        foreach (var f in Directory.GetFiles(_badgeCacheDir, $"*_{BadgeCacheVersion}.png")) {
            try { File.Delete(f); } catch { /* best-effort */ }
        }
        logger.LogInformation("Badge icon cache fully invalidated");
    }

    private string BadgeDiskPath(string ext) =>
        Path.Combine(_badgeCacheDir, $"{ext.TrimStart('.')}_{BadgeCacheVersion}.png");

    private byte[]? TryBadgeDiskCache(string ext) {
        var file = BadgeDiskPath(ext);
        if (!File.Exists(file)) return null;
        var bytes = File.ReadAllBytes(file);
        logger.LogDebug("Badge disk-cache hit ({Bytes} bytes): {Ext}", bytes.Length, ext);
        return bytes;
    }

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (query.Length < AppDefaults.FileSearchMinQueryLength) yield break;
        if (settings.FileSearchVisibility == SearchSourceVisibility.Disabled) yield break;

        const int SnapshotIntervalMs = AppDefaults.FileSearchSnapshotIntervalMs;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var channel = Channel.CreateUnbounded<IReadOnlyList<ResultItemViewModel>>();

        _ = Task.Run(async () => {
            var buffer = new List<ResultItemViewModel>();
            var queryLower = query.ToLowerInvariant();
            var hasWildcard = query.Contains('*');
            var queryTokens = hasWildcard ? [] : queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isMultiToken = queryTokens.Length > 1;
            var lastSnapshot = Environment.TickCount64 - SnapshotIntervalMs;
            var folders = settings.FileSearchOnlySpecificFolders
                ? settings.ExpandedSearchFolders
                : (IReadOnlyList<string>?)null;
            logger.LogDebug("DocSearch start query=\"{Query}\" timeout={TimeoutMs}ms fileSearchVisibility={Visibility} onlySpecificFolders={OnlySpecific} folders=[{Folders}]",
                query, timeoutMs, settings.FileSearchVisibility, settings.FileSearchOnlySpecificFolders,
                folders is null ? "(all)" : string.Join(", ", folders));

            try {
                await fileSearch.SearchAsync(
                    query,
                    r => {
                        var nameLower = r.Name.ToLowerInvariant();
                        var filename = Path.GetFileNameWithoutExtension(nameLower);
                        var extension = Path.GetExtension(nameLower);
                        var score = 0.5;
                        IReadOnlyList<(int Start, int Length)>? titleRanges = null;
                        string? scoreReason = null;
                        if (!hasWildcard) {
                            if (isMultiToken) {
                                if (!queryTokens.All(t => nameLower.Contains(t))) return;
                                var nameSegments = nameLower.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
                                if (queryTokens.All(t => nameSegments.Any(s => s.StartsWith(t)))) {
                                    score = 0.75;
                                    titleRanges = queryTokens
                                        .Select(t => (nameLower.IndexOf(t, StringComparison.Ordinal), t.Length))
                                        .Where(x => x.Item1 >= 0)
                                        .ToList();
                                    scoreReason = "Multi-token (×3.5)";
                                }
                            } else {
                                if (nameLower == queryLower && !string.IsNullOrEmpty(extension)) {
                                    score = 1.1; // nombre completo con extensión (ej. "PC.png" → "PC.png")
                                    titleRanges = [(0, r.Name.Length)];
                                    scoreReason = "Nombre completo (×3.5)";
                                } else if (filename == queryLower && !string.IsNullOrEmpty(extension)) {
                                    score = 1;   // stem exacto con extensión propia (ej. "report.pdf" → "report")
                                    titleRanges = [(0, filename.Length)];
                                    scoreReason = "Nombre sin ext (×3.5)";
                                } else if (extension == $".{queryLower}") {
                                    score = 0.9; // coincidencia de extensión (ej. "photo.png" → "png")
                                    titleRanges = [(filename.Length, extension.Length)];
                                    scoreReason = "Extensión exacta (×3.5)";
                                } else if (nameLower == queryLower) {
                                    score = 0.85; // nombre exacto sin extensión: fichero sin ext (carpeta) (ej. "Documents/" → "Documents", "Makefile" → "Makefile")
                                    titleRanges = [(0, r.Name.Length)];
                                    scoreReason = "Nombre completo (×3.5)";
                                } else if (nameLower.StartsWith(queryLower, StringComparison.Ordinal)) {
                                    score = 0.75;
                                    titleRanges = [(0, queryLower.Length)];
                                    scoreReason = "Prefijo nombre (×3.5)";
                                } else if (filename.StartsWith(queryLower, StringComparison.Ordinal)) {
                                    score = 0.75;
                                    titleRanges = [(0, queryLower.Length)];
                                    scoreReason = "Prefijo fichero (×3.5)";
                                } else if (nameLower.EndsWith(queryLower, StringComparison.Ordinal)) {
                                    score = 0.5;
                                    titleRanges = [(nameLower.Length - queryLower.Length, queryLower.Length)];
                                    scoreReason = "Sufijo (×3.5)";
                                }
                            }
                        }
                        IReadOnlyList<(int Start, int Length)>? subtitleRanges = null;
                        var pathIdx = r.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                        if (pathIdx >= 0)
                            subtitleRanges = [(pathIdx, query.Length)];
                        PreloadBadgeIconAsync(r.Path);
                        var path = r.Path;
                        var ext = Path.GetExtension(r.Name).ToLowerInvariant();
                        var actions = new List<ResultAction> {
                            new() {
                                Label         = "Open",
                                LabelProvider = () => {
                                    var appName = _appNameByExtension.GetValueOrDefault(ext);
                                    return appName != null ? $"Open in {appName}" : "Open";
                                },
                                Hotkey       = ActionHotkey.Enter,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                ClosesWindow = true,
                                Execute      = () => {
                                    logger.LogInformation("DocSearch: open \"{Path}\"", path);
                                    platform.LaunchApp(path);
                                },
                            },
                            new() {
                                Label         = "Open (background)",
                                LabelProvider = () => {
                                    var appName = _appNameByExtension.GetValueOrDefault(ext);
                                    return appName != null ? $"Open in {appName} (background)" : "Open (background)";
                                },
                                Hotkey                  = ActionHotkey.MetaEnter,
                                ShowInFooter            = false,
                                ShowInMenu              = true,
                                ClosesMenu              = true,
                                ClosesWindow            = false,
                                RegainFocusAfterExecute = true,
                                Execute                 = () => {
                                    logger.LogInformation("DocSearch: open \"{Path}\" in background", path);
                                    platform.LaunchApp(path);
                                },
                            },
                            new() {
                                Label        = "Copy path",
                                Hotkey       = ActionHotkey.MetaC,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                HintProvider = () => "Path copied!",
                                Execute      = () => clipboard.CopyText(path),
                            },
                        };
                        if (IsEditableExtension(path)) {
                            actions.Add(new ResultAction {
                                Label        = "Preview",
                                Hotkey       = ActionHotkey.MetaP,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                Execute      = () => { },
                            });
                            actions.Add(new ResultAction {
                                Label        = "Edit",
                                Hotkey       = ActionHotkey.MetaE,
                                ShowInFooter = true,
                                ShowInMenu   = true,
                                ClosesMenu   = true,
                                Execute      = () => { },
                            });
                        }
                        buffer.Add(new FileResultItemViewModel {
                            IconBytes = fileIconCache.Get(r.Path),
                            BadgeIconBytes = _badgeByExtension.GetValueOrDefault(ext),
                            Title = r.Name,
                            Subtitle = r.Path,
                            ItemPath = r.Path,
                            Category = "Files",
                            Score = score * 3.5,
                            ScoreReason = scoreReason,
                            TitleRanges = titleRanges,
                            SubtitleRanges = subtitleRanges,
                            GetDragPayload = () => new DragPayload.File(path),
                            Actions = actions,
                        });

                        var now = Environment.TickCount64;
                        if (now - lastSnapshot >= SnapshotIntervalMs) {
                            lastSnapshot = now;
                            var topItems = buffer.OrderByDescending(x => x.Score).Take(limit).ToList();
                            foreach (var item in topItems)
                                item.IconBytes ??= fileIconCache.GetOrPreload(item.Subtitle);
                            RefreshIconBytes(buffer);
                            channel.Writer.TryWrite(topItems);
                        }
                    },
                    maxResults: int.MaxValue,
                    searchFolders: folders,
                    ct: cts.Token);
                logger.LogInformation("DocSearch done query=\"{Query}\" total={Count}", query, buffer.Count);
            } catch (OperationCanceledException) {
                logger.LogInformation("DocSearch cancelled query=\"{Query}\" results={Count} callerCancelled={CallerCancelled} timeout={Timeout}",
                    query, buffer.Count, ct.IsCancellationRequested, cts.IsCancellationRequested && !ct.IsCancellationRequested);
            }

            cts.Dispose();
            if (buffer.Count > 0) {
                var finalItems = buffer.OrderByDescending(x => x.Score).Take(limit).ToList();
                foreach (var item in finalItems)
                    item.IconBytes ??= fileIconCache.GetOrPreload(item.Subtitle);
                RefreshIconBytes(buffer);
                channel.Writer.TryWrite(finalItems);
            }
            channel.Writer.TryComplete();
        }, CancellationToken.None);

        await foreach (var snapshot in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return snapshot;
    }

    private void RefreshIconBytes(List<ResultItemViewModel> buffer) {
        foreach (var item in buffer) {
            item.IconBytes ??= fileIconCache.Get(item.Subtitle);
            if (item.BadgeIconBytes == null) {
                var ext = Path.GetExtension(item.Subtitle).ToLowerInvariant();
                if (_badgeByExtension.TryGetValue(ext, out var badge))
                    item.BadgeIconBytes = badge;
            }
        }
    }

    private void PreloadBadgeIconAsync(string filePath) {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return;

        // Disk cache hit for badge → load synchronously so the first snapshot already has it.
        if (!_badgeByExtension.ContainsKey(ext)) {
            var diskBytes = TryBadgeDiskCache(ext);
            if (diskBytes != null) _badgeByExtension[ext] = diskBytes;
        }

        // Both already known: nothing to resolve.
        if (_badgeByExtension.ContainsKey(ext) && _appNameByExtension.ContainsKey(ext)) return;

        if (!_badgePreloading.TryAdd(ext, 0)) return;
        Task.Run(() => {
            var appPath = platform.GetDefaultAppPath(filePath);
            logger.LogDebug("Badge [{Ext}] appPath={App}", ext, appPath ?? "(null)");

            var samePath = appPath != null && string.Equals(
                Path.GetFullPath(appPath), Path.GetFullPath(filePath),
                StringComparison.OrdinalIgnoreCase);

            // App name: useful for the "Open in X" label even when the badge is suppressed
            // because the app's icon matches the file icon (e.g. .cs → Rider).
            // Suppressed only when there's no default app, or when the file IS the app itself.
            if (!_appNameByExtension.ContainsKey(ext)) {
                _appNameByExtension[ext] = (appPath == null || samePath)
                    ? null
                    : Path.GetFileNameWithoutExtension(appPath);
                logger.LogDebug("AppName [{Ext}] = {Name}", ext, _appNameByExtension[ext] ?? "(null)");
            }

            // Badge: skip if already loaded from disk cache above.
            if (!_badgeByExtension.ContainsKey(ext)) {
                if (appPath == null) {
                    _badgeByExtension[ext] = null;
                } else if (samePath) {
                    logger.LogDebug("Badge [{Ext}] suppressed: same path", ext);
                    _badgeByExtension[ext] = null;
                } else if (platform.AreIconsSame(filePath, appPath)) {
                    // macOS uses the app icon as the document icon for this file type
                    // (e.g. .cs → Rider, .java → IntelliJ). Compare raw TIFF before normalization.
                    logger.LogDebug("Badge [{Ext}] suppressed: same icon (TIFF)", ext);
                    _badgeByExtension[ext] = null;
                } else {
                    var badgeBytes = platform.GetAppIconBytes(appPath);
                    logger.LogDebug("Badge [{Ext}] loaded {N} bytes", ext, badgeBytes?.Length ?? -1);
                    _badgeByExtension[ext] = badgeBytes;
                    if (badgeBytes is not null) {
                        Directory.CreateDirectory(_badgeCacheDir);
                        File.WriteAllBytes(BadgeDiskPath(ext), badgeBytes);
                    }
                }
            }

            BadgeIconLoaded?.Invoke();
        });
    }

    private bool IsEditableExtension(string filePath) {
        if (!settings.EnableFileEditor) return false;
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return !string.IsNullOrEmpty(ext)
            && settings.FileEditorExtensions.Any(e =>
                e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
}

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
    int timeoutMs = AppDefaults.FileSearchTimeoutMs) : IDeferredSearchSource {

    // Badge icon cache: keyed by lowercase extension; null means "no default app found"
    private readonly ConcurrentDictionary<string, byte[]?> _badgeByExtension = new();
    private readonly ConcurrentDictionary<string, byte> _badgePreloading = new();

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (query.Length < AppDefaults.FileSearchMinQueryLength) yield break;

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
            var folders = settings.ExpandedSearchFolders;
            logger.LogDebug("DocSearch start query=\"{Query}\" timeout={TimeoutMs}ms folders=[{Folders}]",
                query, timeoutMs, string.Join(", ", folders));

            try {
                await fileSearch.SearchAsync(
                    query,
                    r => {
                        var nameLower = r.Name.ToLowerInvariant();
                        var filename = Path.GetFileNameWithoutExtension(nameLower);
                        var extension = Path.GetExtension(nameLower);
                        var score = 0.5;
                        if (!hasWildcard) {
                            if (isMultiToken) {
                                if (!queryTokens.All(t => nameLower.Contains(t))) return;
                                var nameSegments = nameLower.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
                                if (queryTokens.All(t => nameSegments.Any(s => s.StartsWith(t))))
                                    score = 0.75;
                            } else {
                                if (filename == queryLower && !string.IsNullOrEmpty(extension))
                                    score = 1;   // stem exacto con extensión propia (ej. "report.pdf" → "report")
                                else if (extension == $".{queryLower}")
                                    score = 0.9; // coincidencia de extensión (ej. "photo.png" → "png")
                                else if (nameLower == queryLower)
                                    score = 0.85; // nombre completo exacto sin extensión (ej. carpeta "png")
                                else if (nameLower.StartsWith(queryLower, StringComparison.Ordinal) || filename.StartsWith(queryLower, StringComparison.Ordinal))
                                    score = 0.75;
                                else if (nameLower.EndsWith(queryLower, StringComparison.Ordinal))
                                    score = 0.5;
                            }
                        }
                        PreloadBadgeIconAsync(r.Path);
                        var path = r.Path;
                        var ext = Path.GetExtension(r.Name).ToLowerInvariant();
                        buffer.Add(new ResultItemViewModel {
                            IconBytes = fileIconCache.Get(r.Path),
                            BadgeIconBytes = _badgeByExtension.GetValueOrDefault(ext),
                            Title = r.Name,
                            Subtitle = r.Path,
                            Category = "Files",
                            Score = score,
                            OnActivate = () => platform.LaunchApp(path),
                        });

                        var now = Environment.TickCount64;
                        if (now - lastSnapshot >= SnapshotIntervalMs) {
                            lastSnapshot = now;
                            var topItems = buffer.OrderByDescending(x => x.Score).Take(limit).ToList();
                            foreach (var item in topItems)
                                fileIconCache.PreloadAsync(item.Subtitle);
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
                    fileIconCache.PreloadAsync(item.Subtitle);
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
        if (_badgeByExtension.ContainsKey(ext)) return;
        if (!_badgePreloading.TryAdd(ext, 0)) return;
        Task.Run(() => {
            var appPath = platform.GetDefaultAppPath(filePath);
            logger.LogDebug("Badge [{Ext}] appPath={App}", ext, appPath ?? "(null)");
            if (appPath == null) { _badgeByExtension[ext] = null; return; }

            // Same path: the file IS the app (e.g. .app bundles)
            if (string.Equals(Path.GetFullPath(appPath), Path.GetFullPath(filePath),
                    StringComparison.OrdinalIgnoreCase)) {
                logger.LogDebug("Badge [{Ext}] suppressed: same path", ext);
                _badgeByExtension[ext] = null;
                return;
            }

            // Same icon: macOS uses the app icon as the document icon for this file type
            // (e.g. .cs → Rider, .java → IntelliJ). Compare raw TIFF data before normalization.
            if (platform.AreIconsSame(filePath, appPath)) {
                logger.LogDebug("Badge [{Ext}] suppressed: same icon (TIFF)", ext);
                _badgeByExtension[ext] = null;
                return;
            }

            var badgeBytes = platform.GetAppIconBytes(appPath);
            logger.LogDebug("Badge [{Ext}] loaded {N} bytes", ext, badgeBytes?.Length ?? -1);
            _badgeByExtension[ext] = badgeBytes;
        });
    }
}

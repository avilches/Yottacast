using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(
    IEnumerable<IInstantSearchSource> instantSources,
    IEnumerable<IDeferredSearchSource> deferredSources,
    ILogger<GlobalSearch>? logger = null) {

    private readonly IReadOnlyList<IInstantSearchSource> _instantSources = instantSources.ToList();
    private readonly IReadOnlyList<IDeferredSearchSource> _deferredSources = deferredSources.ToList();
    private readonly ILogger<GlobalSearch> _logger = logger ?? NullLogger<GlobalSearch>.Instance;

    public void Start() {
        foreach (var s in _instantSources) s.Start();
        foreach (var s in _deferredSources) s.Start();
    }

    public Task WhenInstantReady() => Task.WhenAll(_instantSources.Select(s => s.WhenReady()));

    public Task WhenReady() => Task.WhenAll(
        _instantSources.Select(s => s.WhenReady())
        .Concat(_deferredSources.Select(s => s.WhenReady())));

    public Task Stop() => Task.WhenAll(
        _instantSources.Select(s => s.Stop())
        .Concat(_deferredSources.Select(s => s.Stop())));

    // ── File-vs-app deduplication ───────────────────────────────────────────────
    // The same app bundle can surface twice: as an app (e.g. Safari at /Applications/Safari.app)
    // and as a file from the document source when that exact bundle lies in an indexed folder.
    // We dedup by PATH, not by name: only a file whose ItemPath equals an app's ItemPath is the
    // very same bundle and gets removed. A distinct document that merely shares a name with an app
    // (e.g. ~/Desktop/Safari.txt) has a different path and is always kept — the user may want it.
    // Centralized here so the GUI (merged list in RefreshResults) and the IPC daemon (deferred file
    // snapshots filtered against the instant app paths) dedup identically.

    /// <summary>Paths (ItemPath) of app results (Category == "Application") in <paramref name="items"/>.</summary>
    public static HashSet<string> AppResultPaths(IEnumerable<BaseResultItemViewModel> items) =>
        items.OfType<ResultItemViewModel>()
            .Where(x => x.Category == "Application" && !string.IsNullOrEmpty(x.ItemPath))
            .Select(x => x.ItemPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Removes file results whose path equals one of <paramref name="appPaths"/> (i.e. the file IS the
    /// same bundle as an app already present). Returns the input unchanged when there is nothing to remove.
    /// </summary>
    public static IReadOnlyList<BaseResultItemViewModel> RemoveFilesDuplicatingApps(
        IReadOnlyList<BaseResultItemViewModel> items, IReadOnlySet<string> appPaths) {
        if (appPaths.Count == 0) return items;
        static bool IsDup(BaseResultItemViewModel x, IReadOnlySet<string> paths) =>
            x is FileResultItemViewModel file && paths.Contains(file.ItemPath);

        var hasDup = false;
        foreach (var x in items)
            if (IsDup(x, appPaths)) { hasDup = true; break; }
        if (!hasDup) return items;

        var result = new List<BaseResultItemViewModel>(items.Count);
        foreach (var x in items)
            if (!IsDup(x, appPaths)) result.Add(x);
        return result;
    }

    /// <summary>Convenience: dedup a single merged list (apps + files) against its own app paths.</summary>
    public static IReadOnlyList<BaseResultItemViewModel> DeduplicateFilesAgainstApps(
        IReadOnlyList<BaseResultItemViewModel> items) =>
        RemoveFilesDuplicatingApps(items, AppResultPaths(items));

    public (IReadOnlyList<BaseResultItemViewModel> Items, string? Hint, SearchHintKind HintKind)
        SearchInstant(string query, int limit, SearchMode mode = SearchMode.All) {

        var activeSources = GetActiveInstantSources(mode);
        var items = activeSources
            .SelectMany(s => {
                var sourceLimit = s.Limit;
                return s.Search(query, sourceLimit < 0 ? limit : sourceLimit);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var hintProvider = activeSources.OfType<ISearchHintProvider>().FirstOrDefault(s => s.LastHint != null);
        var hint = hintProvider?.LastHint;
        var hintKind = hintProvider?.LastHintKind ?? SearchHintKind.Info;
        return (items, hint, hintKind);
    }

    public IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchDeferredAsync(
        string query, int limit, SearchMode mode = SearchMode.All, CancellationToken ct = default)
        => SearchSourcesAsync(GetActiveDeferredSources(mode), query, limit, ct);

    private IReadOnlyList<IInstantSearchSource> GetActiveInstantSources(SearchMode mode)
        => GetActiveSources(_instantSources, mode);

    private IReadOnlyList<IDeferredSearchSource> GetActiveDeferredSources(SearchMode mode)
        => GetActiveSources(_deferredSources, mode);

    private static IReadOnlyList<T> GetActiveSources<T>(IReadOnlyList<T> sources, SearchMode mode)
        where T : class {
        if (mode == SearchMode.All)
            return sources
                .Where(s => s is not ISearchModeSource ms || ms.IsActiveIn(SearchMode.All))
                .ToList();
        return sources
            .OfType<ISearchModeSource>()
            .Where(s => s.IsActiveIn(mode))
            .Cast<T>()
            .ToList();
    }

    /// <summary>
    /// Merges snapshots from all sources. Each source owns a slot; when it emits a new
    /// snapshot the slot is updated and the merged+sorted union is yielded.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchSourcesAsync(
        IReadOnlyList<IDeferredSearchSource> subset, string query, int limit,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var snapshots = new List<BaseResultItemViewModel>[subset.Count];
        for (var i = 0; i < subset.Count; i++) snapshots[i] = [];

        var channel = Channel.CreateUnbounded<(int, IReadOnlyList<BaseResultItemViewModel>)>();

        var tasks = subset.Select((s, i) => Task.Run(async () => {
            try {
                await foreach (var snap in s.SearchAsync(query, limit, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync((i, snap), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected when the search is superseded or cancelled; ignore.
            } catch (Exception ex) {
                _logger.LogError(ex, "Deferred source {Source} failed for query \"{Query}\"",
                    s.GetType().Name, query);
            }
        }, CancellationToken.None)).ToList();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var (idx, snap) in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            snapshots[idx] = snap.ToList();
            yield return snapshots.SelectMany(s => s)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .ToList();
        }
    }
}

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

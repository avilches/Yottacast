using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(IEnumerable<IInstantSearchSource> instantSources, IEnumerable<IDeferredSearchSource> deferredSources) {

    private readonly IReadOnlyList<IInstantSearchSource> _instantSources = instantSources.ToList();
    private readonly IReadOnlyList<IDeferredSearchSource> _deferredSources = deferredSources.ToList();

    public void Start() {
        foreach (var s in _instantSources) s.Start();
        foreach (var s in _deferredSources) s.Start();
    }

    public Task WhenReady() => Task.WhenAll(
        _instantSources.Select(s => s.WhenReady())
        .Concat(_deferredSources.Select(s => s.WhenReady())));

    public Task Stop() => Task.WhenAll(
        _instantSources.Select(s => s.Stop())
        .Concat(_deferredSources.Select(s => s.Stop())));

    public IReadOnlyList<ResultItemViewModel> SearchInstant(string query, int limit) =>
        _instantSources
            .SelectMany(s => s.Search(query, limit))
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

    public IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchDeferredAsync(
        string query, int limit, CancellationToken ct = default)
        => SearchSourcesAsync(_deferredSources, query, limit, ct);

    /// <summary>
    /// Merges snapshots from all sources. Each source owns a slot; when it emits a new
    /// snapshot the slot is updated and the merged+sorted union is yielded.
    /// </summary>
    private static async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchSourcesAsync(
        IReadOnlyList<IDeferredSearchSource> subset, string query, int limit,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var snapshots = new List<ResultItemViewModel>[subset.Count];
        for (var i = 0; i < subset.Count; i++) snapshots[i] = [];

        var channel = Channel.CreateUnbounded<(int, IReadOnlyList<ResultItemViewModel>)>();

        var tasks = subset.Select((s, i) => Task.Run(async () => {
            try {
                await foreach (var snap in s.SearchAsync(query, limit, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync((i, snap), ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { }
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

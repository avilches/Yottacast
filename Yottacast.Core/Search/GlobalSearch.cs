using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(IEnumerable<ISearchSource> sources) {

    private readonly IReadOnlyList<ISearchSource> _sources = sources.ToList();

    public bool IsInstant => false;

    public void Start() { foreach (var s in _sources) s.Start(); }

    public Task WhenReady() => Task.WhenAll(_sources.Select(s => s.WhenReady()));

    public Task Stop() => Task.WhenAll(_sources.Select(s => s.Stop()));

    public IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchInstantAsync(
        string query, int limit, CancellationToken ct = default)
        => SearchSourcesAsync(_sources.Where(s => s.IsInstant).ToList(), query, limit, ct);

    public IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchDeferredAsync(
        string query, int limit, CancellationToken ct = default)
        => SearchSourcesAsync(_sources.Where(s => !s.IsInstant).ToList(), query, limit, ct);

    /// <summary>
    /// Merges snapshots from all sources. Each source owns a slot; when it emits a new
    /// snapshot the slot is updated and the merged+sorted union is yielded.
    /// </summary>
    private static async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchSourcesAsync(
        IReadOnlyList<ISearchSource> subset, string query, int limit,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var snapshots = new List<ResultItemViewModel>[subset.Count];
        for (var i = 0; i < subset.Count; i++) snapshots[i] = [];

        var channel = Channel.CreateUnbounded<(int, IReadOnlyList<ResultItemViewModel>)>();

        var tasks = subset.Select((s, i) => Task.Run(async () => {
            try {
                await s.WhenReady().ConfigureAwait(false);
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

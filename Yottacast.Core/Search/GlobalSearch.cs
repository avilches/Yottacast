using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(IEnumerable<ISearchSource> sources) {

    private readonly IReadOnlyList<ISearchSource> _sources = sources.ToList();

    public bool IsInstant => false;

    public void Start() { foreach (var s in _sources) s.Start(); }

    public Task Ready() => Task.WhenAll(_sources.Select(s => s.Ready()));

    public Task Stop() => Task.WhenAll(_sources.Select(s => s.Stop()));

    public IAsyncEnumerable<ResultItemViewModel> SearchInstantAsync(
        string query, int limit, CancellationToken ct = default)
        => SearchSourcesAsync(_sources.Where(s => s.IsInstant), query, limit, ct);

    public IAsyncEnumerable<ResultItemViewModel> SearchDeferredAsync(
        string query, int limit, CancellationToken ct = default)
        => SearchSourcesAsync(_sources.Where(s => !s.IsInstant), query, limit, ct);

    /// <summary>
    /// Merges results from the given subset of sources in real-time via a channel,
    /// yielding items as each source produces them. Sources run concurrently.
    /// </summary>
    private static async IAsyncEnumerable<ResultItemViewModel> SearchSourcesAsync(
        IEnumerable<ISearchSource> subset, string query, int limit,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var channel = Channel.CreateUnbounded<ResultItemViewModel>();

        var tasks = subset.Select(async s => {
            try {
                await foreach (var item in s.SearchAsync(query, limit, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { }
        }).ToList();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return item;
    }
}

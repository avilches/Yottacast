using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch {
    private readonly IEnumerable<ISearchSource> _sources;

    public GlobalSearch(IEnumerable<ISearchSource> sources) {
        _sources = sources;
    }

    public Task Start() => Task.WhenAll(_sources.Select(s => s.Start()));

    public void Stop() {
        foreach (var source in _sources)
            source.Stop();
    }

    /// <summary>
    /// Merges results from all sources in real-time via a channel, yielding items
    /// as each source produces them. Sources run concurrently.
    /// </summary>
    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, [EnumeratorCancellation] CancellationToken ct = default) {

        var channel = Channel.CreateUnbounded<ResultItemViewModel>();

        var tasks = _sources.Select(async s => {
            try {
                await foreach (var item in s.SearchAsync(query, ct).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) { }
        }).ToList();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return item;
    }
}

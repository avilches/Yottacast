using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class GlobalSearch(IEnumerable<ISearchSource> sources) : ISearchSource {
    
    public Task Start() => Task.WhenAll(sources.Select(s => s.Start()));

    public Task Stop() => Task.WhenAll(sources.Select(s => s.Stop()));

    /// <summary>
    /// Merges results from all sources in real-time via a channel, yielding items
    /// as each source produces them. Sources run concurrently.
    /// </summary>
    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, [EnumeratorCancellation] CancellationToken ct = default) {

        var channel = Channel.CreateUnbounded<ResultItemViewModel>();

        var tasks = sources.Select(async s => {
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

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// ISearchSource that searches user files via FileSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// Results are streamed incrementally as FileSearch emits them.
/// </summary>
public class UserDocumentSearch(UserSettings settings, FileSearch fileSearch) : ISearchSource {
    public void Start() { }

    public Task Ready() => Task.CompletedTask;

    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        var channel = Channel.CreateUnbounded<ResultItemViewModel>();

        var searchTask = fileSearch.SearchAsync(
            query,
            r => channel.Writer.TryWrite(new ResultItemViewModel {
                Icon = "📄",
                Title = r.Name,
                Subtitle = r.Path,
                Category = "Files",
                Score = 1,
            }),
            maxResults: limit,
            searchFolders: settings.SearchFolders,
            ct: ct);

        _ = searchTask.ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return item;
    }
}

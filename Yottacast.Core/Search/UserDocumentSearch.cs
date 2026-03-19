using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// ISearchSource that searches user files via UserDocumentSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// Results are streamed incrementally as UserDocumentSearch emits them.
/// </summary>
public class UserDocumentSearch(UserSettings settings) : ISearchSource {
    public void Start() { }

    public Task Ready() => Task.CompletedTask;

    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, [EnumeratorCancellation] CancellationToken ct = default) {

        var channel = Channel.CreateUnbounded<ResultItemViewModel>();

        var searchTask = FileSearch.SearchAsync(
            query,
            r => channel.Writer.TryWrite(new ResultItemViewModel {
                Icon = "📄",
                Title = r.Name,
                Subtitle = r.Path,
                Category = "Files",
                Score = 1,
            }),
            maxResults: 15,
            searchFolders: settings.SearchFolders,
            ct: ct);

        _ = searchTask.ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return item;
    }
}

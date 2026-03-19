using System.Runtime.CompilerServices;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// ISearchSource that searches user files via FileSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// Results are streamed incrementally as FileSearch emits them.
/// </summary>
public class UserDocumentSearch(UserSettings settings, FileSearch fileSearch) : ISearchSource {
    public bool IsInstant => false;

    public void Start() { }

    public Task Ready() => Task.CompletedTask;

    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<ResultItemViewModel> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        const double EarlyExitThreshold = 2.0;
        const int TimeoutMs = 400;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeoutMs);

        var buffer = new List<ResultItemViewModel>();
        var queryLower = query.ToLowerInvariant();
        var goodCount = 0;

        try {
            await fileSearch.SearchAsync(
                query,
                r => {
                    var isDir = Directory.Exists(r.Path);
                    var nameLower = r.Name.ToLowerInvariant();
                    double score = 0.5;
                    if (isDir) score += 1.0;
                    if (nameLower == queryLower) score += 2.0;
                    else if (nameLower.StartsWith(queryLower, StringComparison.Ordinal)) score += 0.5;
                    buffer.Add(new ResultItemViewModel {
                        Icon = isDir ? "📁" : "📄",
                        Title = r.Name,
                        Subtitle = r.Path,
                        Category = isDir ? "Folders" : "Files",
                        Score = score,
                    });
                    if (score >= EarlyExitThreshold && ++goodCount >= limit)
                        cts.Cancel();
                },
                maxResults: 500,
                searchFolders: settings.ExpandedSearchFolders,
                ct: cts.Token);
        } catch (OperationCanceledException) { }

        foreach (var item in buffer.OrderByDescending(x => x.Score).Take(limit))
            yield return item;
    }
}

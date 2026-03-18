using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public class DocumentSearch {
    private readonly IEnumerable<ISearchSource> _sources;

    public DocumentSearch(IEnumerable<ISearchSource> sources) {
        _sources = sources;
    }

    public Task Start() => Task.WhenAll(_sources.Select(s => s.Start()));

    public void Stop() {
        foreach (var source in _sources)
            source.Stop();
    }

    public async Task<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, CancellationToken ct = default) {
        var tasks = _sources.Select(s => s.SearchAsync(query, ct));
        var results = await Task.WhenAll(tasks);
        return results
            .SelectMany(r => r)
            .OrderByDescending(r => r.Score)
            .ToList();
    }
}

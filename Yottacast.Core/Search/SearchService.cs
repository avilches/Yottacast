using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.ViewModels;
using Yottacast.ViewModels;

namespace Yottacast.Core.Search;

public class SearchService {
    private readonly IEnumerable<ISearchSource> _sources;

    public SearchService(IEnumerable<ISearchSource> sources) {
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

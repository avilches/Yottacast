using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface IDeferredSearchSource {
    Task Stop();
    IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, int limit, CancellationToken ct = default);
}
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface IDeferredSearchSource {
    void Start();
    Task WhenReady();
    Task Stop();
    IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, int limit, CancellationToken ct = default);
}
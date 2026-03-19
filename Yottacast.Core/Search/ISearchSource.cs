using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface ISearchSource {
    /// <summary>Starts background scanning. Always fire-and-forget; never await the return value.</summary>
    void Start();
    /// <summary>Returns a Task that completes once the initial scan is done and the source is ready to serve queries.</summary>
    Task Ready();
    Task Stop();
    IAsyncEnumerable<ResultItemViewModel> SearchAsync(string query, int limit, CancellationToken ct = default);
}

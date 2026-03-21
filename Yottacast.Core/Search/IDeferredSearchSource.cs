using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface IDeferredSearchSource {
    /// <summary>Starts background scanning. Always fire-and-forget; never await the return value.</summary>
    void Start();
    /// <summary>Returns a Task that completes once the initial scan is done and the source is ready to serve queries.</summary>
    Task WhenReady();
    Task Stop();
    IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, int limit, CancellationToken ct = default);
}
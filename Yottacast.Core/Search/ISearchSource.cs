using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface ISearchSource {
    Task Start(); 
    Task Stop();
    IAsyncEnumerable<ResultItemViewModel> SearchAsync(string query, CancellationToken ct = default);
}

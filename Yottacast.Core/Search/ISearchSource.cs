using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

public interface ISearchSource {
    Task Start();
    void Stop();
    IAsyncEnumerable<ResultItemViewModel> SearchAsync(string query, CancellationToken ct = default);
}

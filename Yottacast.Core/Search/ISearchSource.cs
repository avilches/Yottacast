using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.ViewModels;
using Yottacast.ViewModels;

namespace Yottacast.Core.Search;

public interface ISearchSource {
    Task Start();
    void Stop();
    Task<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, CancellationToken ct = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yottacast.Core.Search;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Storage;

/// <summary>
/// ISearchSource that searches user files via FileSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// </summary>
public class FileStorage : ISearchSource {
    private readonly UserSettings _settings;

    public FileStorage(UserSettings settings) {
        _settings = settings;
    }

    public Task Start() => Task.CompletedTask;

    public void Stop() { }

    public async Task<IReadOnlyList<ResultItemViewModel>> SearchAsync(string query, CancellationToken ct = default) {
        var results = new List<ResultItemViewModel>();
        await FileSearch.SearchAsync(
            query,
            r => results.Add(new ResultItemViewModel {
                Icon = "📄",
                Title = r.Name,
                Subtitle = r.Path,
                Category = "Files",
                Score = 0,
            }),
            maxResults: 15,
            searchFolders: _settings.SearchFolders,
            ct: ct);
        return results;
    }
}

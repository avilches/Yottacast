using Microsoft.Extensions.Logging;
using Yottacast.Core.Platform;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.LocalPath;

public class LocalPathSearch(
    FileIconCache fileIconCache,
    PlatformProvider platform,
    ClipboardService clipboard,
    ILogger<LocalPathSearch> logger) : IInstantSearchSource {

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _limit) {
        if (!IsLocalPath(query)) return [];
        var expanded = ExpandPath(query);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return [];

        var title = Path.GetFileName(expanded);
        if (string.IsNullOrEmpty(title)) title = expanded;

        logger.LogDebug("LocalPathSearch: found \"{Path}\"", expanded);

        var capturedPath = expanded;
        return [new ResultItemViewModel {
            IconBytes      = fileIconCache.GetOrPreload(expanded),
            Title          = title,
            Subtitle       = expanded,
            Category       = "Files",
            Score          = 1.0,
            OnActivate     = () => {
                logger.LogInformation("LocalPath: open \"{Path}\"", capturedPath);
                platform.LaunchApp(capturedPath);
            },
            OnCopy         = () => clipboard.CopyText(capturedPath),
            CopiedMessage  = "Path copied!",
        }];
    }

    /// <summary>Returns true if the query looks like a local filesystem path.</summary>
    public static bool IsLocalPath(string query) {
        if (string.IsNullOrEmpty(query)) return false;
        if (query[0] == '/' || query.StartsWith("~/") ||
            query.StartsWith("./") || query.StartsWith("../"))
            return true;
        // Windows: C:\ or D:/
        return query.Length >= 3
               && query[1] == ':'
               && (query[2] == '\\' || query[2] == '/')
               && char.IsLetter(query[0]);
    }

    private static string ExpandPath(string path) {
        if (path == "~" || path.StartsWith("~/"))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.Length > 2 ? path[2..] : "");
        return path;
    }
}

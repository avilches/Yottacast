using Yottacast.Core.Platform;
using Yottacast.Core.Process;

namespace Yottacast.Core.Services;

public record FileResult(string Name, string Path);

/// <summary>
/// Searches user documents using the OS native index, streaming results in real-time.
/// Running the subprocess under a PTY (default) forces line-buffering, so results arrive
/// as they are found instead of filling a pipe buffer first.
///   macOS   → Spotlight (mdfind)
///   Windows → Windows Search Index (via PowerShell + OLE DB)
///   Linux   → plocate / locate
/// </summary>
public class FileSearch(PlatformProvider platform) {
    public Task SearchAsync(
        string query, Action<FileResult> onResult, int maxResults = 10,
        RunnerBackend backend = RunnerBackend.Pty,
        IReadOnlyList<string>? searchFolders = null,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(query)) return Task.CompletedTask;
        return platform.SearchFilesAsync(query, onResult, maxResults, backend, searchFolders, ct);
    }
}

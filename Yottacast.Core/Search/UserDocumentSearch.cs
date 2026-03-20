using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// ISearchSource that searches user files via FileSearch, scoped to the folders
/// configured in UserSettings (Downloads, Desktop, Documents, Movies, Pictures by default).
/// Yields progressive snapshots as FileSearch emits results — each snapshot is the current
/// best-N sorted results seen so far. Snapshots are emitted every SnapshotEvery results
/// and once after mdfind finishes or the timeout triggers.
/// </summary>
public class UserDocumentSearch(
    UserSettings settings,
    FileSearch fileSearch,
    ILogger<UserDocumentSearch> logger,
    int timeoutMs = 20_000) : ISearchSource {

    public bool IsInstant => false;

    public void Start() { }

    public Task Ready() => Task.CompletedTask;

    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        const int SnapshotIntervalMs = 200;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var channel = Channel.CreateUnbounded<IReadOnlyList<ResultItemViewModel>>();

        _ = Task.Run(async () => {
            var buffer = new List<ResultItemViewModel>();
            var queryLower = query.ToLowerInvariant();
            var hasWildcard = query.Contains('*');
            var lastSnapshot = Environment.TickCount64 - SnapshotIntervalMs;
            var folders = settings.ExpandedSearchFolders;
            logger.LogDebug("DocSearch start query=\"{Query}\" timeout={TimeoutMs}ms folders=[{Folders}]",
                query, timeoutMs, string.Join(", ", folders));

            try {
                await fileSearch.SearchAsync(
                    query,
                    r => {
                        var isDir = Directory.Exists(r.Path);
                        var nameLower = r.Name.ToLowerInvariant();
                        var stemLower = Path.GetFileNameWithoutExtension(nameLower);
                        double score = 0.5;
                        if (!hasWildcard) {
                            if (nameLower == queryLower || stemLower == queryLower)
                                score += 2.0;
                            else if (nameLower.StartsWith(queryLower, StringComparison.Ordinal) || stemLower.StartsWith(queryLower, StringComparison.Ordinal))
                                score += 1.0;
                            else if (nameLower.EndsWith(queryLower, StringComparison.Ordinal))
                                score += 0.3;
                        }
                        buffer.Add(new ResultItemViewModel {
                            Icon = isDir ? "📁" : "📄",
                            Title = r.Name,
                            Subtitle = r.Path,
                            Category = isDir ? "Folders" : "Files",
                            Score = score,
                        });

                        var now = Environment.TickCount64;
                        if (now - lastSnapshot >= SnapshotIntervalMs) {
                            lastSnapshot = now;
                            channel.Writer.TryWrite(
                                buffer.OrderByDescending(x => x.Score).Take(limit).ToList());
                        }
                    },
                    maxResults: int.MaxValue,
                    searchFolders: folders,
                    ct: cts.Token);
                logger.LogInformation("DocSearch done query=\"{Query}\" total={Count}", query, buffer.Count);
            } catch (OperationCanceledException) {
                logger.LogInformation("DocSearch cancelled query=\"{Query}\" results={Count} callerCancelled={CallerCancelled} timeout={Timeout}",
                    query, buffer.Count, ct.IsCancellationRequested, cts.IsCancellationRequested && !ct.IsCancellationRequested);
            }

            cts.Dispose();
            channel.Writer.TryWrite(buffer.OrderByDescending(x => x.Score).Take(limit).ToList());
            channel.Writer.TryComplete();
        }, CancellationToken.None);

        await foreach (var snapshot in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return snapshot;
    }
}

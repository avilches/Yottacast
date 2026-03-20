using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// Instant search source activated by queries starting with ':'.
/// Typing ':' shows the 6 emojis with the lowest Unicode sort_order; typing ':smile' filters by name/keyword.
/// Emoji data is loaded from the embedded resource; a compact cache is written to cacheDir for fast startups.
/// Activating a result copies the emoji character to the clipboard.
/// </summary>
public class EmojiSearch(ClipboardService clipboard, string cacheDir, EmojiDataLoader dataLoader, ILogger<EmojiSearch> logger) : ISearchSource {

    private Task<IReadOnlyList<EmojiEntry>>? _loadTask;
    private volatile IReadOnlyList<EmojiEntry> _entries = [];

    public bool IsInstant => true;

    public void Start() {
        _loadTask = Task.Run(() => dataLoader.LoadAsync(cacheDir));
        _ = _loadTask.ContinueWith(
            t => { if (!t.IsFaulted) _entries = t.Result; },
            TaskScheduler.Default);
    }

    public Task WhenReady() => _loadTask ?? Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        if (!query.StartsWith(':')) yield break;

        var term = query[1..].Trim().ToLowerInvariant();
        var results = string.IsNullOrEmpty(term)
            ? GetDefaultResults()
            : FilterEmojis(term, limit);

        if (results.Count > 0) yield return results;

        await Task.CompletedTask;
    }

    private IReadOnlyList<ResultItemViewModel> GetDefaultResults() =>
        _entries
            .Where(e => e.SortOrder > 0)
            .OrderBy(e => e.SortOrder)
            .Take(6)
            .Select(e => MakeResult(e, 3.5))
            .ToList();

    private IReadOnlyList<ResultItemViewModel> FilterEmojis(string term, int limit) =>
        _entries
            .Select(e => (entry: e, score: MatchScore(e, term)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => MakeResult(x.entry, 3.5))
            .ToList();

    private ResultItemViewModel MakeResult(EmojiEntry e, double score) {
        var c = e.Char;
        return new ResultItemViewModel {
            Icon = e.Char,
            Title = e.Name,
            Subtitle = "Press Enter to copy and paste",
            Category = "Emoji",
            Score = score,
            OnActivate = () => clipboard.CopyText(c),
            PasteAfterActivate = true,
        };
    }

    private static double MatchScore(EmojiEntry e, string term) {
        if (e.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 6;
        if (e.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 5;
        if (e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 4;
        if (e.Keywords.Any(k => k.Equals(term, StringComparison.OrdinalIgnoreCase))) return 3;
        if (e.Keywords.Any(k => k.StartsWith(term, StringComparison.OrdinalIgnoreCase))) return 2;
        if (e.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase))) return 1;
        return 0;
    }
}
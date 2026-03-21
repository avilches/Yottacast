using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Emoji;

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
        var emojis = string.IsNullOrEmpty(term)
            ? GetDefaultEmojis()
            : FilterEmojis(term, limit);

        if (emojis.Count > 0) yield return [MakeGrid(emojis)];

        await Task.CompletedTask;
    }

    private IReadOnlyList<EmojiEntry> GetDefaultEmojis() =>
        _entries
            .Where(e => e.SortOrder > 0)
            .OrderBy(e => e.SortOrder)
            .Take(6)
            .ToList();

    private IReadOnlyList<EmojiEntry> FilterEmojis(string term, int limit) =>
        _entries
            .Select(e => (entry: e, score: MatchScore(e, term)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(limit)
            .Select(x => x.entry)
            .ToList();

    private EmojiGridResultViewModel MakeGrid(IReadOnlyList<EmojiEntry> emojis) {
        var cells = emojis.Select((e, i) => new EmojiCellViewModel {
            Char = e.Char,
            Name = e.Name,
            IsSelected = i == 0,
        }).ToList();

        EmojiGridResultViewModel grid = null!;
        grid = new EmojiGridResultViewModel {
            Cells = cells,
            Score = 3.5,
            PasteAfterActivate = true,
            OnActivate = () => clipboard.CopyText(grid.Cells[grid.SelectedEmojiIndex].Char),
            OnLeft  = () => grid.SelectPrevious(),
            OnRight = () => grid.SelectNext(),
        };
        return grid;
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
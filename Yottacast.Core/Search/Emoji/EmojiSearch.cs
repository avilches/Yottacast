using Microsoft.Extensions.Logging;
using Yottacast.Core.Search.Application;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Emoji;

/// <summary>
/// Instant search source activated by queries starting with ':'.
/// Typing ':' shows the emojis with the lowest Unicode sort_order; typing ':smile' filters by name/keyword.
/// Emoji data is loaded from the embedded resource; a compact cache is written to disk for fast startups.
/// Activating a result copies the emoji character to the clipboard.
/// </summary>
public class EmojiSearch(ClipboardService clipboard, string emojiCachePath, EmojiDataLoader dataLoader, ILogger<EmojiSearch> logger) : IInstantSearchSource {

    private Task<IReadOnlyList<EmojiEntry>>? _loadTask;
    private volatile IReadOnlyList<EmojiEntry> _entries = [];

    public void Start() {
        _loadTask = Task.Run(async () => {
            var entries = await dataLoader.LoadAsync(emojiCachePath);
            _entries = entries;
            return entries;
        });
    }

    public Task WhenReady() => _loadTask ?? Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!query.StartsWith(':')) return [];

        var term = query[1..].Trim().ToLowerInvariant();
        var emojis = string.IsNullOrEmpty(term)
            ? GetDefaultEmojis()
            : FilterEmojis(term, limit);

        if (emojis.Count > 0) return [MakeGrid(emojis)];

        return [];
    }

    private IReadOnlyList<EmojiEntry> GetDefaultEmojis() =>
        _entries
            .Where(e => e.SortOrder > 0)
            .OrderBy(e => e.SortOrder)
            .Take(AppDefaults.EmojiDefaultLimit)
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
            Char     = e.Char,
            Name     = e.Name,
            Category = e.Category,
            Keywords = e.Keywords,
            IsSelected = i == 0,
        }).ToList();

        EmojiGridResultViewModel grid = null!;
        grid = new EmojiGridResultViewModel {
            Cells    = cells,
            Icon     = cells.Count > 0 ? cells[0].Char : "",
            Title    = cells.Count > 0 ? cells[0].Name : "",
            Category = "Emoji",
            Score    = 3.5,
            PasteAfterActivate = true,
            OnActivate = () => clipboard.CopyText(grid.Cells[grid.SelectedEmojiIndex].Char),
            OnLeft  = () => { grid.SelectPrevious(); return true; },
            OnRight = () => { grid.SelectNext(); return true; },
            OnUp    = () => grid.SelectUp(),
            OnDown  = () => grid.SelectDown(),
        };
        return grid;
    }

    // Exact name match → 3.0; other name matches → NameMatcher score + 1 (range 1.2–2.0);
    // keyword-only matches → NameMatcher score (0–1). Ensures "fire" ranks above "fireworks"
    // when both names start with the same query ("fire"), since both would otherwise score 1.0.
    private static double MatchScore(EmojiEntry e, string term) {
        if (e.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 3.0;
        var nameScore = NameMatcher.Score(e.NameTokens, e.Name, term);
        if (nameScore > 0) return nameScore + 1;
        return e.Keywords.Select(k => NameMatcher.Score(k, term)).DefaultIfEmpty(0d).Max();
    }
}

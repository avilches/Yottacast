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
public class EmojiSearch(ClipboardService clipboard, string emojiCachePath, EmojiDataLoader dataLoader, EmojiUsageStore usageStore, EmojiLayoutConfig emojiLayoutConfig, ILogger<EmojiSearch> logger, UserSettings settings) : IInstantSearchSource {

    private Task<IReadOnlyList<EmojiEntry>>? _loadTask;
    private volatile IReadOnlyList<EmojiEntry> _entries = [];

    public int Limit => AppDefaults.EmojiSearchLimit;

    public void Start() {
        _loadTask = Task.Run(async () => {
            var entries = await dataLoader.LoadAsync(emojiCachePath);
            _entries = entries;
            await usageStore.LoadAsync();
            return entries;
        });
    }

    public Task WhenReady() => _loadTask ?? Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int limit) {
        if (!settings.EnableEmoji) return [];
        if (!query.StartsWith(':')) return [];

        var term = query[1..].Trim().ToLowerInvariant();
        var emojis = string.IsNullOrEmpty(term)
            ? GetDefaultEmojis()
            : FilterEmojis(term, limit).Select(e => (e, EmojiSection.Default)).ToList();

        if (emojis.Count > 0) return [MakeGrid(emojis)];

        return [];
    }

    private IReadOnlyList<(EmojiEntry Entry, EmojiSection Section)> GetDefaultEmojis() {
        var charToEntry = _entries.ToDictionary(e => e.Char);
        var result = new List<(EmojiEntry Entry, EmojiSection Section)>();

        // Favorites first, capped at EmojiMaxFavorites (4 cells).
        // Favorites are NOT added to seen — they also appear in their Default position below.
        var favSet = new HashSet<string>(usageStore.Favorites);
        foreach (var ch in usageStore.Favorites.Take(AppDefaults.EmojiMaxFavorites)) {
            if (charToEntry.TryGetValue(ch, out var entry))
                result.Add((entry, EmojiSection.Favorite));
        }

        // Most-used next, filling up to effectivePinnedTotal cells, excluding favorites.
        // effectivePinnedTotal rounds EmojiMaxPinnedTotal up to the nearest full grid row so the
        // pinned section never ends mid-row (e.g. 10 min with 8 cols → 16; with 12 cols → 12).
        var cols = emojiLayoutConfig.Columns;
        var effectivePinnedTotal = ((AppDefaults.EmojiMaxPinnedTotal + cols - 1) / cols) * cols;
        var mostUsedMax = effectivePinnedTotal - usageStore.Favorites.Count;
        // Most-used also appear in their Default position below.
        var seenMostUsed = new HashSet<string>();
        foreach (var ch in usageStore.GetMostUsed(mostUsedMax)) {
            if (!favSet.Contains(ch) && charToEntry.TryGetValue(ch, out var entry) && seenMostUsed.Add(ch))
                result.Add((entry, EmojiSection.MostUsed));
        }

        // All emojis in normal sort order — no exclusions.
        // Favorites and MostUsed also appear here in their natural category position.
        foreach (var entry in _entries.Where(e => e.SortOrder > 0).OrderBy(e => e.SortOrder))
            result.Add((entry, EmojiSection.Default));

        return result;
    }

    private IReadOnlyList<EmojiEntry> FilterEmojis(string term, int limit) =>
        _entries
            .Select(e => (entry: e, score: MatchScore(e, term)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.entry)
            .ToList();

    private EmojiGridResultViewModel MakeGrid(IReadOnlyList<(EmojiEntry Entry, EmojiSection Section)> emojis) {
        var cells = emojis.Select((x, i) => new EmojiCellViewModel {
            Char     = x.Entry.Char,
            Name     = x.Entry.Name,
            Category = x.Entry.Category,
            Keywords = x.Entry.Keywords,
            Section  = x.Section,
            UsageCount = usageStore.GetUsageCount(x.Entry.Char),
            IsSelected = i == 0,
            IsFavorite = usageStore.IsFavorite(x.Entry.Char),
        }).ToList();

        var hasPinned = emojis.Any(x => x.Section is EmojiSection.Favorite or EmojiSection.MostUsed);

        EmojiGridResultViewModel grid = null!;
        grid = new EmojiGridResultViewModel {
            Cells       = cells,
            Icon        = cells.Count > 0 ? cells[0].Char : "",
            Title       = cells.Count > 0 ? cells[0].Name : "",
            Category    = "Emoji",
            Score       = 5.5,
            ScoreReason = "Grid de emojis",
            Columns      = emojiLayoutConfig.Columns,
            ViewportRows = emojiLayoutConfig.ViewportRows,
            HasPinnedSection = hasPinned,
            PinnedSectionHeader = hasPinned ? "Favorites & recently used" : "",
            Actions = [
                new() {
                    Label           = "Paste",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    HintProvider    = () => {
                        var cell = grid.Cells[grid.SelectedEmojiIndex];
                        return $"Emoji {cell.Char} copied!";
                    },
                    Execute = () => {
                        var cell = grid.Cells[grid.SelectedEmojiIndex];
                        logger.LogInformation("Emoji: copied {Char} ({Name})", cell.Char, cell.Name);
                        clipboard.CopyText(cell.Char);
                        usageStore.RecordUsage(cell.Char);
                    },
                },
                new() {
                    Label        = "Copy",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => {
                        var cell = grid.Cells[grid.SelectedEmojiIndex];
                        return $"Emoji {cell.Char} copied!";
                    },
                    Execute = () => {
                        var cell = grid.Cells[grid.SelectedEmojiIndex];
                        logger.LogInformation("Emoji: copied (no paste) {Char} ({Name})", cell.Char, cell.Name);
                        clipboard.CopyText(cell.Char);
                        usageStore.RecordUsage(cell.Char);
                    },
                },
                new() {
                    Label           = "Favorite",
                    Hotkey          = ActionHotkey.MetaShiftF,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = false,
                    ClosesWindow    = false,
                    RequiresRefresh = true,
                    Execute = () => {
                        var cell = grid.Cells[grid.SelectedEmojiIndex];
                        usageStore.ToggleFavorite(cell.Char);
                        var isFav = usageStore.IsFavorite(cell.Char);
                        foreach (var c in grid.Cells.Where(c => c.Char == cell.Char))
                            c.IsFavorite = isFav;
                        logger.LogInformation("Emoji: favorite toggled {Char} ({Name}) -> {IsFav}",
                            cell.Char, cell.Name, isFav);
                    },
                },
            ],
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
    // Multi-word queries (e.g. "flag sp"): all tokens must match; score is the minimum across tokens.
    private static double MatchScore(EmojiEntry e, string term) {
        var terms = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 1) return SingleTermScore(e, term);

        var min = double.MaxValue;
        foreach (var t in terms) {
            var s = SingleTermScore(e, t);
            if (s <= 0) return 0;
            if (s < min) min = s;
        }
        return min == double.MaxValue ? 0 : min;
    }

    private static double SingleTermScore(EmojiEntry e, string term) {
        if (e.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 3.0;
        var nameScore = NameMatcher.Score(e.NameTokens, e.Name, term);
        if (nameScore > 0) return nameScore + 1;
        var keywordScore = e.Keywords.Select(k => NameMatcher.Score(k, term)).DefaultIfEmpty(0d).Max();
        if (keywordScore > 0) return keywordScore;
        return NameMatcher.Score(e.Category, term) * 0.5; // category match scores lower than keywords
    }
}

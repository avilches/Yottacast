using System.Runtime.CompilerServices;
using System.Text.Json;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// Instant search source activated by queries starting with ':'.
/// Typing ':' shows 6 popular default emojis; typing ':smile' filters by name/keyword.
/// Activating a result copies the emoji character to the clipboard.
/// </summary>
public class EmojiSearch(ClipboardService clipboard) : ISearchSource {

    private record EmojiEntry(string Char, string Name, string[] Keywords);

    private static readonly Lazy<IReadOnlyList<EmojiEntry>> Entries = new(LoadEmojis);

    // 6 emojis shown when only ":" is typed (no search term yet)
    private static readonly string[] Defaults = ["😀", "❤️", "👍", "🎉", "🔥", "✨"];

    public bool IsInstant => true;
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
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
        Defaults
            .Select(c => Entries.Value.FirstOrDefault(e => e.Char == c))
            .OfType<EmojiEntry>()
            .Select(e => MakeResult(e, 3.5))
            .ToList();

    private IReadOnlyList<ResultItemViewModel> FilterEmojis(string term, int limit) =>
        Entries.Value
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

    private static IReadOnlyList<EmojiEntry> LoadEmojis() {
        using var stream = typeof(EmojiSearch).Assembly
            .GetManifestResourceStream("Yottacast.Core.Resources.emojis.json")!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray()
            .Select(item => new EmojiEntry(
                item[0].GetString()!,
                item[1].GetString()!,
                item[2].GetString()!.Split(',')))
            .ToList();
    }
}

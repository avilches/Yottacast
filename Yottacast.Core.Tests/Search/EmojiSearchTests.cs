using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class EmojiSearchTests {

    private static async Task<EmojiSearch> BuildSearchWithCache(string compactJson) {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "emoji-cache.json"), compactJson);
        var search = new EmojiSearch(new ClipboardService(), dir, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), NullLogger<EmojiSearch>.Instance);
        search.Start();
        await search.WhenReady();
        return search;
    }

    private static IReadOnlyList<Yottacast.Core.ViewModels.ResultItemViewModel> SearchResults(
        EmojiSearch search, string query) {
        return search.Search(query, 10);
    }

    // ── Default results ───────────────────────────────────────────────────────

    [Fact]
    public async Task ColonOnly_ReturnsDefaultResults_OrderedBySortOrder() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["😃","grinning face with big eyes",["smiley"],"Smileys & Emotion",2],
          ["😄","grinning face with smiling eyes",["smile"],"Smileys & Emotion",3],
          ["😁","beaming face with smiling eyes",["grin"],"Smileys & Emotion",4],
          ["😆","grinning squinting face",["laughing"],"Smileys & Emotion",5],
          ["😅","grinning face with sweat",["sweat_smile"],"Smileys & Emotion",6],
          ["🤣","rolling on the floor laughing",["rofl"],"Smileys & Emotion",7]
        ]
        """;

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":");

        // EmojiSearch returns one grid item whose cells are the emojis ordered by sort_order
        var grid = Assert.IsType<EmojiGridResultViewModel>(Assert.Single(results));
        Assert.Equal(7, grid.Cells.Count);
        Assert.Equal("😀", grid.Cells[0].Char);
        Assert.Equal("😃", grid.Cells[1].Char);
    }

    [Fact]
    public async Task ColonOnly_ReturnsEmpty_WhenNoEntries() {
        var search = await BuildSearchWithCache("[]");
        var results = SearchResults(search, ":");
        Assert.Empty(results);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ColonTerm_FiltersByName() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["👍","thumbs up sign",["thumbsup","+1"],"People & Body",10]
        ]
        """;

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":thumbs");

        Assert.Single(results);
        Assert.Equal("👍", results[0].Icon);
    }

    [Fact]
    public async Task ColonTerm_FiltersByShortName() {
        var json = """
        [
          ["👍","thumbs up sign",["thumbsup","+1"],"People & Body",10],
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1]
        ]
        """;

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":thumbsup");

        Assert.Single(results);
        Assert.Equal("👍", results[0].Icon);
    }

    [Fact]
    public async Task ColonTerm_FiltersByAsciiText() {
        var json = """
        [
          ["😀","grinning face",[":D",":-D"],"Smileys & Emotion",1],
          ["👍","thumbs up sign",["thumbsup"],"People & Body",10]
        ]
        """;

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, "::D");

        Assert.Single(results);
        Assert.Equal("😀", results[0].Icon);
    }

    [Fact]
    public async Task ColonTerm_ExactNameScoresHighest() {
        var json = """
        [
          ["🔥","fire",["fire"],"Travel & Places",1],
          ["🎆","fireworks",["fireworks"],"Travel & Places",2]
        ]
        """;

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":fire");

        // Both match, returned as one grid; exact name "fire" must be first cell
        var grid = Assert.IsType<EmojiGridResultViewModel>(Assert.Single(results));
        Assert.Equal(2, grid.Cells.Count);
        Assert.Equal("🔥", grid.Cells[0].Char); // exact match wins over prefix
    }

    [Fact]
    public async Task NonColonQuery_ReturnsNothing() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, "grinning");

        Assert.Empty(results);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_HasCorrectShape() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";

        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":");

        var item = Assert.Single(results);
        Assert.Equal("😀", item.Icon);
        Assert.Equal("grinning face", item.Title);
        Assert.Equal("Emoji", item.Category);
        Assert.Equal(3.5, item.Score);
        Assert.True(item.PasteAfterActivate);
    }

    // ── Result shape (Icon/Title/Category set from first cell) ────────────────

    [Fact]
    public async Task Result_IconAndTitle_ReflectFirstCell() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var search = await BuildSearchWithCache(json);
        var results = SearchResults(search, ":");
        var item = Assert.Single(results);
        Assert.Equal("😀", item.Icon);
        Assert.Equal("grinning face", item.Title);
        Assert.Equal("Emoji", item.Category);
    }

    [Fact]
    public async Task OnActivate_CopiesCharToClipboard() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var clipboard = new ClipboardService();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "emoji-cache.json"), json);
        var search = new EmojiSearch(clipboard, dir, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), NullLogger<EmojiSearch>.Instance);
        search.Start();
        await search.WhenReady();

        string copied = "";
        clipboard.Initialize(text => copied = text);

        var results = SearchResults(search, ":");
        var item = Assert.Single(results);
        Assert.NotNull(item.OnActivate);
        item.OnActivate();

        Assert.Equal("😀", copied);
    }
}

// ── Integration tests against the real embedded emoji-data.json ───────────────
//
// These tests load the full emoji dataset from the embedded resource (no cache)
// to verify that the matching logic works against production data.
// The fixture loads the data once and shares it across all tests in the class.

public class RealEmojiDataFixture : IAsyncLifetime, IDisposable {
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"emoji-real-{Guid.NewGuid()}");

    public EmojiSearch Search { get; private set; } = null!;

    public async Task InitializeAsync() {
        Directory.CreateDirectory(_tempDir);
        var loader = new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance);
        // First call: loads from embedded resource and writes the cache.
        await loader.LoadAsync(_tempDir);
        // EmojiSearch then reads the cache on Start(), so data loads quickly.
        Search = new EmojiSearch(
            new ClipboardService(), _tempDir, loader,
            NullLogger<EmojiSearch>.Instance);
        Search.Start();
        await Search.WhenReady();
    }

    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}

public class EmojiSearchRealDataTests(RealEmojiDataFixture fixture)
    : IClassFixture<RealEmojiDataFixture> {

    private static EmojiGridResultViewModel? SearchGrid(EmojiSearch search, string query) {
        var results = search.Search(query, 20);
        return results.OfType<EmojiGridResultViewModel>().FirstOrDefault();
    }

    // ── Prefix / exact / keyword ──────────────────────────────────────────────

    [Fact]
    public void Prefix_GrinReturnsGrinningEmojis() {
        var grid = SearchGrid(fixture.Search, ":grin");
        Assert.NotNull(grid);
        Assert.All(grid.Cells, c => Assert.Contains("grin", c.Name));
    }

    [Fact]
    public void Keyword_ThumbsupReturnsThumbsUpEmoji() {
        // "thumbsup" is a short_name keyword for 👍
        var grid = SearchGrid(fixture.Search, ":thumbsup");
        Assert.NotNull(grid);
        Assert.Contains(grid.Cells, c => c.Name.Contains("thumbs up"));
    }

    [Fact]
    public void Exact_FireReturnsFireFirst() {
        // "fire" is the exact name of 🔥; other emojis may contain "fire" in keywords
        var grid = SearchGrid(fixture.Search, ":fire");
        Assert.NotNull(grid);
        Assert.Equal("fire", grid.Cells[0].Name); // exact match must be first
    }

    // ── Multi-word abbreviation (new tier 0.4) ────────────────────────────────

    [Fact]
    public void MultiWordAbbrev_SmilaFindsSmilingFace() {
        // "smifa" = smi→"smiling" + fa→"face"
        var grid = SearchGrid(fixture.Search, ":smifa");
        Assert.NotNull(grid);
        Assert.Contains(grid.Cells, c => c.Name.StartsWith("smiling face"));
    }

    [Fact]
    public void MultiWordAbbrev_GrfaFindsGrinningFace() {
        // "grfa" = gr→"grinning" + fa→"face"
        var grid = SearchGrid(fixture.Search, ":grfa");
        Assert.NotNull(grid);
        Assert.Contains(grid.Cells, c => c.Name.StartsWith("grinning face"));
    }

    [Fact]
    public void MultiWordAbbrev_NameRanksAboveKeyword() {
        // "grinning" appears both as a name prefix and as a keyword;
        // the emoji whose NAME starts with the query should rank first.
        var grid = SearchGrid(fixture.Search, ":grinning");
        Assert.NotNull(grid);
        Assert.Contains("grinning", grid.Cells[0].Name);
    }
}

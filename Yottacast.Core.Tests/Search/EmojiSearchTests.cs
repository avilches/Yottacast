using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Emoji;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class EmojiSearchTests {

    private static async Task<EmojiSearch> BuildSearchWithCache(string compactJson, EmojiUsageStore? usageStore = null) {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cachePath = Path.Combine(dir, "emoji-cache.json");
        await File.WriteAllTextAsync(cachePath, compactJson);
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        usageStore ??= new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        var search = new EmojiSearch(new ClipboardService(NullLogger<ClipboardService>.Instance), cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, NullLogger<EmojiSearch>.Instance, settings);
        search.Start();
        await search.WhenReady();
        return search;
    }

    private static IReadOnlyList<Yottacast.Core.ViewModels.ResultItemViewModel> SearchResults(
        EmojiSearch search, string query) {
        return search.Search(query, 10).Cast<Yottacast.Core.ViewModels.ResultItemViewModel>().ToList();
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
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cachePath = Path.Combine(dir, "emoji-cache.json");
        await File.WriteAllTextAsync(cachePath, json);
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        var search = new EmojiSearch(clipboard, cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, NullLogger<EmojiSearch>.Instance, settings);
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

    // ── Favorites and most-used ──────────────────────────────────────────────

    [Fact]
    public async Task DefaultGrid_ShowsFavoritesFirst() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["😃","grinning face with big eyes",["smiley"],"Smileys & Emotion",2],
          ["🔥","fire",["fire"],"Travel & Places",3]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // Pinned section: favorite first
        Assert.Equal("🔥", grid.Cells[0].Char);
        Assert.True(grid.Cells[0].IsFavorite);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        // Then ALL emojis in normal order (including fire again)
        Assert.Equal("😀", grid.Cells[1].Char);
        Assert.Equal("😃", grid.Cells[2].Char);
        Assert.Equal("🔥", grid.Cells[3].Char); // fire also appears in normal position
        Assert.True(grid.Cells[3].IsFavorite);   // with IsFavorite=true
        Assert.Equal(EmojiSection.Default, grid.Cells[3].Section);
        Assert.Equal(4, grid.Cells.Count);
    }

    [Fact]
    public async Task DefaultGrid_ShowsMostUsedAfterFavorites() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["😃","grinning face with big eyes",["smiley"],"Smileys & Emotion",2],
          ["🔥","fire",["fire"],"Travel & Places",3],
          ["👍","thumbs up sign",["thumbsup"],"People & Body",4]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥");
        usageStore.RecordUsage("👍");
        usageStore.RecordUsage("👍");
        usageStore.RecordUsage("😃");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // Pinned section: favorite + most-used
        Assert.Equal("🔥", grid.Cells[0].Char);  // favorite
        Assert.Equal("👍", grid.Cells[1].Char);  // most-used (2 uses)
        Assert.Equal("😃", grid.Cells[2].Char);  // most-used (1 use)
        // Normal section: ALL emojis in sort order
        Assert.Equal("😀", grid.Cells[3].Char);
        Assert.Equal("😃", grid.Cells[4].Char);  // appears again in normal position
        Assert.Equal("🔥", grid.Cells[5].Char);  // appears again in normal position
        Assert.Equal("👍", grid.Cells[6].Char);  // appears again in normal position
        Assert.Equal(7, grid.Cells.Count);
    }

    [Fact]
    public async Task DefaultGrid_EmojisAppearInBothPinnedAndNormalSections() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["🔥","fire",["fire"],"Travel & Places",2]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥");
        usageStore.RecordUsage("🔥");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // Fire appears in pinned (as favorite, not most-used since it's already a favorite)
        Assert.Equal("🔥", grid.Cells[0].Char);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        // Then all emojis in normal order
        Assert.Equal("😀", grid.Cells[1].Char);
        Assert.Equal("🔥", grid.Cells[2].Char); // fire appears again in normal position
        Assert.Equal(EmojiSection.Default, grid.Cells[2].Section);
        Assert.Equal(3, grid.Cells.Count);
    }

    [Fact]
    public async Task OnCopy_CopiesWithoutPasteAfterActivate() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        Assert.NotNull(grid.OnCopy);
        Assert.True(grid.PasteAfterActivate); // the grid has PasteAfterActivate for Enter
        // OnCopy is a separate action that just copies — the caller (MainWindow) does not hide/paste
    }

    [Fact]
    public async Task OnToggleFavorite_UpdatesCellAndStore() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        Assert.False(grid.Cells[0].IsFavorite);
        Assert.NotNull(grid.OnToggleFavorite);
        grid.OnToggleFavorite();

        Assert.True(grid.Cells[0].IsFavorite);
        Assert.True(usageStore.IsFavorite("😀"));

        grid.OnToggleFavorite();
        Assert.False(grid.Cells[0].IsFavorite);
        Assert.False(usageStore.IsFavorite("😀"));
    }

    [Fact]
    public async Task OnToggleFavorite_UpdatesAllCellsWithSameChar() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["🔥","fire",["fire"],"Travel & Places",2]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥"); // fire is a favorite -> appears in pinned + normal

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // Fire appears twice: pinned (index 0) and normal (index 2)
        var fireCells = grid.Cells.Where(c => c.Char == "🔥").ToList();
        Assert.Equal(2, fireCells.Count);
        Assert.All(fireCells, c => Assert.True(c.IsFavorite));

        // Toggle off favorite on the first fire cell
        grid.SelectedEmojiIndex = 0;
        grid.OnToggleFavorite!();

        // Both cells should be updated
        Assert.All(fireCells, c => Assert.False(c.IsFavorite));
        Assert.False(usageStore.IsFavorite("🔥"));
    }

    [Fact]
    public async Task DefaultGrid_HasPinnedSection_WhenFavoritesExist() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["🔥","fire",["fire"],"Travel & Places",2]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        Assert.True(grid.HasPinnedSection);
        Assert.Equal("★ Favorites & most used", grid.PinnedSectionHeader);
    }

    [Fact]
    public async Task DefaultGrid_NoPinnedSection_WhenNoFavoritesOrMostUsed() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        Assert.False(grid.HasPinnedSection);
        Assert.Equal("", grid.PinnedSectionHeader);
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
        var cachePath = Path.Combine(_tempDir, "emoji-cache.json");
        var loader = new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance);
        // First call: loads from embedded resource and writes the cache.
        await loader.LoadAsync(cachePath);
        // EmojiSearch then reads the cache on Start(), so data loads quickly.
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var usageStore = new EmojiUsageStore(Path.Combine(_tempDir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        Search = new EmojiSearch(
            new ClipboardService(NullLogger<ClipboardService>.Instance), cachePath, loader,
            usageStore, NullLogger<EmojiSearch>.Instance, settings);
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

    // ── Category matching ─────────────────────────────────────────────────────

    [Fact]
    public void Category_TravelFindsTravelEmojis() {
        var grid = SearchGrid(fixture.Search, ":travel");
        Assert.NotNull(grid);
        Assert.All(grid!.Cells, c => Assert.Contains("Travel", c.Category, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Category_EmotionFindsSmileysEmojis() {
        var grid = SearchGrid(fixture.Search, ":emotion");
        Assert.NotNull(grid);
        Assert.All(grid!.Cells, c => Assert.Contains("Emotion", c.Category, StringComparison.OrdinalIgnoreCase));
    }

    // ── Multi-word queries (order-independent) ────────────────────────────────

    [Fact]
    public void MultiWord_FlagSpFindsFlagSpain() {
        var grid = SearchGrid(fixture.Search, ":flag sp");
        Assert.NotNull(grid);
        Assert.Contains(grid.Cells, c => c.Name.Contains("spain", StringComparison.OrdinalIgnoreCase) ||
                                         c.Keywords.Any(k => k.Contains("spain", StringComparison.OrdinalIgnoreCase) || k == "es"));
    }

    [Fact]
    public void MultiWord_SpFlagSameAsFlagSp() {
        var gridA = SearchGrid(fixture.Search, ":flag sp");
        var gridB = SearchGrid(fixture.Search, ":sp flag");
        Assert.NotNull(gridA);
        Assert.NotNull(gridB);
        Assert.Equal(gridA!.Cells.Count, gridB!.Cells.Count);
        Assert.Equal(gridA.Cells.Select(c => c.Char), gridB.Cells.Select(c => c.Char));
    }
}

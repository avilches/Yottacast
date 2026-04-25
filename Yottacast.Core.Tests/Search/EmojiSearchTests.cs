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

        // Favorite section first
        Assert.Equal("🔥", grid.Cells[0].Char);
        Assert.True(grid.Cells[0].IsFavorite);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        // Then ALL emojis in sort order in Default section (including fire again)
        Assert.Equal("😀", grid.Cells[1].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[1].Section);
        Assert.Equal("😃", grid.Cells[2].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[2].Section);
        Assert.Equal("🔥", grid.Cells[3].Char);
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

        // Favorite section
        Assert.Equal("🔥", grid.Cells[0].Char);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        // Most-used section (excluding favorite 🔥)
        Assert.Equal("👍", grid.Cells[1].Char);  // most-used (2 uses)
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[1].Section);
        Assert.Equal("😃", grid.Cells[2].Char);  // most-used (1 use)
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[2].Section);
        // Default section: ALL emojis in sort order (no exclusions)
        Assert.Equal("😀", grid.Cells[3].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[3].Section);
        Assert.Equal("😃", grid.Cells[4].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[4].Section);
        Assert.Equal("🔥", grid.Cells[5].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[5].Section);
        Assert.Equal("👍", grid.Cells[6].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[6].Section);
        Assert.Equal(7, grid.Cells.Count);
    }

    [Fact]
    public async Task DefaultGrid_FavoriteAppearsInBothFavoritesAndDefault() {
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

        // Fire appears in Favorites section...
        Assert.Equal("🔥", grid.Cells[0].Char);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        // ...AND also in its normal Default position
        Assert.Equal("😀", grid.Cells[1].Char);
        Assert.Equal(EmojiSection.Default, grid.Cells[1].Section);
        Assert.Equal("🔥", grid.Cells[2].Char);
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
        // OnCopy copies and records usage; MainWindow hides the window (without paste) after invoking it
    }

    [Fact]
    public async Task DefaultGrid_CellsHaveCorrectSections() {
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

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // [0] 🔥 Favorite, [1] 👍 MostUsed, [2..5] all 4 emojis in Default (no exclusions)
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section); // 🔥 favorite
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[1].Section); // 👍 most-used
        Assert.Equal(EmojiSection.Default,  grid.Cells[2].Section); // 😀 default
        Assert.Equal(EmojiSection.Default,  grid.Cells[3].Section); // 😃 default
        Assert.Equal(EmojiSection.Default,  grid.Cells[4].Section); // 🔥 also in default
        Assert.Equal(EmojiSection.Default,  grid.Cells[5].Section); // 👍 also in default
        Assert.Equal(6, grid.Cells.Count);
    }

    [Fact]
    public async Task DefaultGrid_VisibleSectionsGroupByCategory() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["😃","grinning face with big eyes",["smiley"],"Smileys & Emotion",2],
          ["🔥","fire",["fire"],"Travel & Places",3],
          ["👍","thumbs up sign",["thumbsup"],"People & Body",4]
        ]
        """;
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        var sections = grid.VisibleSections;
        Assert.Equal(3, sections.Count);
        Assert.Equal("Smileys & Emotion", sections[0].Header);
        Assert.Equal(2, sections[0].Cells.Count);
        Assert.Equal("Travel & Places", sections[1].Header);
        Assert.Single(sections[1].Cells);
        Assert.Equal("People & Body", sections[2].Header);
        Assert.Single(sections[2].Cells);
    }

    [Fact]
    public async Task DefaultGrid_VisibleSectionsIncludeFavoriteHeader() {
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

        var sections = grid.VisibleSections;
        Assert.Equal("\u2605 Favorites", sections[0].Header);
        Assert.Equal("🔥", sections[0].Cells[0].Char);
        Assert.Equal("Smileys & Emotion", sections[1].Header);
    }

    [Fact]
    public async Task DefaultGrid_CellsHaveUsageCount() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.RecordUsage("😀");
        usageStore.RecordUsage("😀");
        usageStore.RecordUsage("😀");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        Assert.Equal(3, grid.Cells[0].UsageCount);
        Assert.True(grid.Cells[0].HasUsageCount);
        Assert.Equal("3", grid.Cells[0].UsageCountText);
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
    public async Task OnToggleFavorite_UnfavoriteUpdatesAllFireCells() {
        var json = """
        [
          ["😀","grinning face",["grinning"],"Smileys & Emotion",1],
          ["🔥","fire",["fire"],"Travel & Places",2]
        ]
        """;
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("🔥"); // fire is a favorite

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 10).OfType<EmojiGridResultViewModel>().First();

        // Fire appears in both Favorites and Default sections
        var fireCells = grid.Cells.Where(c => c.Char == "🔥").ToList();
        Assert.Equal(2, fireCells.Count);
        Assert.All(fireCells, c => Assert.True(c.IsFavorite));

        // Toggle off favorite: both cells are updated
        grid.SelectedEmojiIndex = 0;
        grid.OnToggleFavorite!();

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

    // ── Section-aware navigation ─────────────────────────────────────────────

    [Fact]
    public async Task SelectDown_CrossesSectionBoundary() {
        // Favorites: 2 emojis. Default section starts at index 2 (favorites also appear there).
        var json = MakeEmojiJson(15);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("E1");
        usageStore.ToggleFavorite("E2");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // Start at index 0 (first favorite), col 0
        Assert.Equal("E1", grid.Cells[0].Char);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);

        // Down should go to first cell of default section (index 2), not index 0+Columns
        grid.SelectDown();
        Assert.Equal(2, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.Default, grid.Cells[2].Section);
    }

    [Fact]
    public async Task SelectUp_CrossesSectionBoundary() {
        var json = MakeEmojiJson(15);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("E1");
        usageStore.ToggleFavorite("E2");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // Default section starts at index 2 (E1 and E2 are in Favorites, then all 15 in Default)
        grid.SelectedEmojiIndex = 2;
        Assert.Equal(EmojiSection.Default, grid.Cells[2].Section);

        // Up from first row of default section should go to favorites
        grid.SelectUp();
        Assert.Equal(0, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
    }

    [Fact]
    public async Task SelectDown_ClampsColumnToShorterSection() {
        // Favorites: 2 emojis. Default starts at index 2.
        // From col 5 in Default row 0 (index 7), going up clamps to Favorites col 1 (index 1).
        // Going back down should go to Default col 1 (index 3), not col 5.
        var json = MakeEmojiJson(25);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("E1");
        usageStore.ToggleFavorite("E2");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // Default section starts at index 2; col 5 = index 2+5 = 7
        grid.SelectedEmojiIndex = 7;
        Assert.Equal(EmojiSection.Default, grid.Cells[7].Section);

        // Up: favorites has 2 items, clamps to col 1 (index 1)
        grid.SelectUp();
        Assert.Equal(1, grid.SelectedEmojiIndex);

        // Down: should go to col 1 of Default (index 2+1=3), NOT col 5
        grid.SelectDown();
        Assert.Equal(3, grid.SelectedEmojiIndex);
    }

    [Fact]
    public async Task SelectDown_FromLastRowOfSection_GoesToNextSection() {
        // 3 favorites + 3 most-used = 6 cells in the combined pinned section (1 row, since 6 < 10 columns).
        // SelectDown from pinned row 0 should jump directly to Default (no second pinned row).
        var json = MakeEmojiJson(30);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        usageStore.ToggleFavorite("E1");
        usageStore.ToggleFavorite("E2");
        usageStore.ToggleFavorite("E3");
        usageStore.RecordUsage("E4"); usageStore.RecordUsage("E4");
        usageStore.RecordUsage("E5"); usageStore.RecordUsage("E5");
        usageStore.RecordUsage("E6");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // Layout: [0-2] Favorites, [3-5] MostUsed (same combined section), [6+] Default
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[3].Section);
        int defaultStart = 6;
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart].Section);

        // Favorites and MostUsed share the same visual section (6 cells = 1 row).
        // From combined section col 1 (index 1), down → Default col 1 (index defaultStart+1).
        grid.SelectedEmojiIndex = 1;
        grid.SelectDown();
        Assert.Equal(defaultStart + 1, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart + 1].Section);
    }

    [Fact]
    public async Task SelectDown_WithinCombinedPinnedSection_TwoRows() {
        // 4 favorites (max) + 10 most-used (max) = 14 cells in combined pinned section (2 rows: 10+4).
        // SelectDown from row 0 should go to row 1 within the combined section.
        var json = MakeEmojiJson(30);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        for (int i = 1; i <= 4; i++) usageStore.ToggleFavorite($"E{i}");
        for (int i = 5; i <= 14; i++) usageStore.RecordUsage($"E{i}");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // [0-3] Favorites, [4-13] MostUsed (same combined section, 14 cells = 2 rows), [14+] Default
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[4].Section);
        int defaultStart = 14;
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart].Section);

        // From combined section col 1 (index 1), down → row 1, col 1 (index 11)
        grid.SelectedEmojiIndex = 1;
        grid.SelectDown();
        Assert.Equal(11, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[11].Section);

        // From combined section row 1, col 1 (index 11), down → Default col 1 (index defaultStart+1)
        grid.SelectDown();
        Assert.Equal(defaultStart + 1, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart + 1].Section);
    }

    private static string MakeEmojiJson(int count) {
        var entries = Enumerable.Range(1, count)
            .Select(i => $"""["E{i}","emoji {i}",["kw{i}"],"Cat A",{i}]""");
        return "[" + string.Join(",", entries) + "]";
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

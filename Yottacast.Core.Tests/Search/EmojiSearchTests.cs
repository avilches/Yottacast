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
        var search = new EmojiSearch(new ClipboardService(NullLogger<ClipboardService>.Instance), cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, new EmojiLayoutConfig(), NullLogger<EmojiSearch>.Instance, settings);
        search.Start();
        await search.WhenReady();
        return search;
    }

    private static async Task<(EmojiSearch Search, ClipboardService Clipboard, EmojiUsageStore UsageStore, Func<string?> GetLastCopied)> CreateSearchAsync() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var cachePath = Path.Combine(dir, "emoji-cache.json");
        await File.WriteAllTextAsync(cachePath, json);
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        string copied = "";
        clipboard.Initialize(copy: text => copied = text, read: () => Task.FromResult<string?>(null));
        var search = new EmojiSearch(clipboard, cachePath, new EmojiDataLoader(NullLogger<EmojiDataLoader>.Instance), usageStore, new EmojiLayoutConfig(), NullLogger<EmojiSearch>.Instance, settings);
        search.Start();
        await search.WhenReady();
        return (search, clipboard, usageStore, () => copied);
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
        Assert.Equal(5.5, item.Score);
        Assert.NotEmpty(item.Actions);
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
    public async Task PasteAction_CopiesAndRecordsUsage() {
        var (search, _, usageStore, getLastCopied) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var paste = result.Actions.Single(a => a.Label == "Paste");
        var char0 = result.Cells[0].Char;

        paste.Execute();

        Assert.Equal(char0, getLastCopied());
        Assert.True(usageStore.GetUsageCount(char0) > 0);
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
    public async Task CopyAction_HasNoClosesWindow() {
        var (search, _, _, _) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var copy = result.Actions.Single(a => a.Label == "Copy");

        Assert.False(copy.ClosesWindow);
        Assert.True(copy.ShowInFooter);
        Assert.True(copy.ShowInMenu);
    }

    [Fact]
    public async Task CopyAction_HasDynamicHint() {
        var (search, _, _, _) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var copy = result.Actions.Single(a => a.Label == "Copy");
        var char0 = result.Cells[0].Char;

        var hint = copy.HintProvider?.Invoke();

        Assert.Equal($"Emoji {char0} copied!", hint);
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
        // Non-last sections are padded to a full row; count only real (non-placeholder) cells.
        Assert.Equal(2, sections[0].Cells.Count(c => !c.IsPlaceholder));
        Assert.Equal("Travel & Places", sections[1].Header);
        Assert.Equal(1, sections[1].Cells.Count(c => !c.IsPlaceholder));
        Assert.Equal("People & Body", sections[2].Header);
        Assert.Single(sections[2].Cells); // last section: no padding
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
        Assert.Equal("Favorites & recently used", sections[0].Header);
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
    public async Task PasteAction_HasPasteAfterClose() {
        var (search, _, _, _) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var paste = result.Actions.Single(a => a.Label == "Paste");

        Assert.True(paste.PasteAfterClose);
        Assert.True(paste.ClosesWindow);
    }

    [Fact]
    public async Task FavoriteAction_TogglesFavoriteInStoreAndCells() {
        var (search, _, usageStore, _) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var fav = result.Actions.Single(a => a.Label == "Favorite");
        var char0 = result.Cells[0].Char;

        Assert.False(result.Cells[0].IsFavorite);
        fav.Execute();
        Assert.True(usageStore.IsFavorite(char0));
        Assert.True(result.Cells[0].IsFavorite);

        fav.Execute();
        Assert.False(usageStore.IsFavorite(char0));
        Assert.False(result.Cells[0].IsFavorite);
    }

    [Fact]
    public async Task FavoriteAction_HasRequiresRefresh() {
        var (search, _, _, _) = await CreateSearchAsync();
        var result = (EmojiGridResultViewModel)search.Search(":", 10).Single();
        var fav = result.Actions.Single(a => a.Label == "Favorite");

        Assert.True(fav.RequiresRefresh);
        Assert.False(fav.ClosesWindow);
        Assert.False(fav.ClosesMenu);
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
        var fav = grid.Actions.Single(a => a.Label == "Favorite");

        // Fire appears in both Favorites and Default sections
        var fireCells = grid.Cells.Where(c => c.Char == "🔥").ToList();
        Assert.Equal(2, fireCells.Count);
        Assert.All(fireCells, c => Assert.True(c.IsFavorite));

        // Toggle off favorite: both cells are updated
        grid.SelectedEmojiIndex = 0;
        fav.Execute();

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
        Assert.Equal("Favorites & recently used", grid.PinnedSectionHeader);
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
        // 4 favorites (max) + 6 most-used (10 - 4 = 6) = 10 cells in combined pinned section (1 row of 10).
        // SelectDown from row 0 goes directly to Default section (no second pinned row).
        var json = MakeEmojiJson(30);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);
        for (int i = 1; i <= 4; i++) usageStore.ToggleFavorite($"E{i}");
        for (int i = 5; i <= 14; i++) usageStore.RecordUsage($"E{i}");

        var search = await BuildSearchWithCache(json, usageStore);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // [0-3] Favorites, [4-9] MostUsed (combined section, 10 cells = 1 row), [10+] Default
        Assert.Equal(EmojiSection.Favorite, grid.Cells[0].Section);
        Assert.Equal(EmojiSection.MostUsed, grid.Cells[4].Section);
        int defaultStart = 10;
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart].Section);

        // From combined section col 1 (index 1), down → Default col 1 (index defaultStart+1)
        grid.SelectedEmojiIndex = 1;
        grid.SelectDown();
        Assert.Equal(defaultStart + 1, grid.SelectedEmojiIndex);
        Assert.Equal(EmojiSection.Default, grid.Cells[defaultStart + 1].Section);
    }

    private static string MakeEmojiJson(int count) {
        var entries = Enumerable.Range(1, count)
            .Select(i => $"""["E{i}","emoji {i}",["kw{i}"],"Cat A",{i}]""");
        return "[" + string.Join(",", entries) + "]";
    }

    // Each category gets a letter prefix (A=first cat, B=second, ...) so chars are unique.
    private static string MakeEmojiJsonMultiCategory(params (int count, string cat)[] categories) {
        int sortOrder = 1;
        int catIdx = 0;
        var entries = categories.SelectMany(c => {
            var prefix = (char)('A' + catIdx++);
            return Enumerable.Range(1, c.count)
                .Select(_ => $"""["{prefix}{sortOrder}","emoji {sortOrder}",["kw"],"{c.cat}",{sortOrder++}]""");
        });
        return "[" + string.Join(",", entries) + "]";
    }

    // ── Viewport section-tail fix ─────────────────────────────────────────────

    [Fact]
    public async Task VisibleSections_SkipsSectionTailAtTop_WhenScrolledDown() {
        // Cat A: 12 cells (tail = A11-A12, 2 cells in the last partial row).
        // Cat B: 90 cells.
        // No pinned. EmojiViewportRows=8, Columns=10 → defaultVisibleRows=8.
        // Navigate to Cat B cell (index 80) → section-row-aligned DOWN scroll snaps past Cat A's
        // partial tail → _viewportStartCell lands at Cat B start (index 12), Cat A is NOT at top.
        var json = MakeEmojiJsonMultiCategory((12, "Cat A"), (90, "Cat B"));
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        Assert.Equal(102, grid.Cells.Count);

        // Navigate to Default index 80 (Cat B[68]) → triggers scroll
        grid.SelectedEmojiIndex = 80;

        var sections = grid.VisibleSections;

        // Cat A tail must NOT appear as an orphan row at the top.
        Assert.DoesNotContain(sections, s => s.Header == "Cat A");
        Assert.Equal("Cat B", sections[0].Header);
    }

    [Fact]
    public async Task VisibleSections_PadsSectionTailToFullRow_WhenTailIsAtTop() {
        // Same layout. Navigate UP into Cat A's tail (index 10) → tail IS visible
        // (selected cell is there) and is padded to a full row (no orphan visual).
        var json = MakeEmojiJsonMultiCategory((12, "Cat A"), (90, "Cat B"));
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        // Scroll down first, then navigate UP into Cat A's tail
        grid.SelectedEmojiIndex = 80;  // scroll down
        grid.SelectedEmojiIndex = 10;  // scroll up into Cat A tail

        var sections = grid.VisibleSections;

        // Cat A tail must be visible (selected cell is there) and padded to a full row.
        var catA = sections.FirstOrDefault(s => s.Header == "Cat A");
        Assert.NotNull(catA);
        Assert.Equal(AppDefaults.EmojiColumns, catA.Cells.Count); // padded to one full row
        Assert.False(catA.Cells[0].IsPlaceholder); // A11 is real
        Assert.False(catA.Cells[1].IsPlaceholder); // A12 is real
        Assert.All(catA.Cells.Skip(2), c => Assert.True(c.IsPlaceholder));
    }

    [Fact]
    public async Task VisibleSections_ShowsTailAtTop_WhenSelectedCellIsInTail() {
        // Same layout. First scroll DOWN (cell 90) → _viewportStartRow=2.
        // Then scroll UP (cell 10, Cat A tail) → _viewportStartRow=1, defaultStart=10.
        // Selected cell IS in tail → must NOT be skipped.
        var json = MakeEmojiJsonMultiCategory((12, "Cat A"), (90, "Cat B"));
        var search = await BuildSearchWithCache(json);
        var grid = search.Search(":", 100).OfType<EmojiGridResultViewModel>().First();

        grid.SelectedEmojiIndex = 90; // scroll down first
        grid.SelectedEmojiIndex = 10; // scroll up into Cat A tail

        var sections = grid.VisibleSections;

        // Cat A tail must be visible because the selected cell is there.
        Assert.Contains(sections, s => s.Header == "Cat A");
        Assert.Contains(sections[0].Cells, c => c.Char == "A10" || c.Char == "A11");
    }

    [Fact]
    public async Task GetDefaultEmojis_PinnedNeverExceedsTen_WhenMaxFavorites() {
        // 4 favorites + 6 most-used (10 - 4 = 6) = 10 pinned cells total.
        var json = MakeEmojiJson(20);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var usageStore = new EmojiUsageStore(Path.Combine(dir, "emoji-usage.json"), NullLogger<EmojiUsageStore>.Instance);

        for (int i = 1; i <= 4; i++) usageStore.ToggleFavorite($"E{i}");
        for (int i = 5; i <= 14; i++) usageStore.RecordUsage($"E{i}");

        var search = await BuildSearchWithCache(json, usageStore);
        var results = search.Search(":", 100);
        Assert.Single(results);

        var grid = Assert.IsType<EmojiGridResultViewModel>(results[0]);
        var pinnedCount = grid.Cells.Count(c => c.Section is EmojiSection.Favorite or EmojiSection.MostUsed);

        Assert.Equal(10, pinnedCount);
        Assert.Equal(4, grid.Cells.Count(c => c.Section == EmojiSection.Favorite));
        Assert.Equal(6, grid.Cells.Count(c => c.Section == EmojiSection.MostUsed));
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
            usageStore, new EmojiLayoutConfig(), NullLogger<EmojiSearch>.Instance, settings);
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

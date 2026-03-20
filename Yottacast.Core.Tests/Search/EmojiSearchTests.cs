using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Services;

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

    private static async Task<IReadOnlyList<Yottacast.Core.ViewModels.ResultItemViewModel>> SearchAsync(
        EmojiSearch search, string query) {
        await foreach (var snapshot in search.SearchAsync(query, 10))
            return snapshot;
        return [];
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
        var results = await SearchAsync(search, ":");

        Assert.Equal(6, results.Count);
        Assert.Equal("😀", results[0].Icon);
        Assert.Equal("😃", results[1].Icon);
    }

    [Fact]
    public async Task ColonOnly_ReturnsEmpty_WhenNoEntries() {
        var search = await BuildSearchWithCache("[]");
        var results = await SearchAsync(search, ":");
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
        var results = await SearchAsync(search, ":thumbs");

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
        var results = await SearchAsync(search, ":thumbsup");

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
        var results = await SearchAsync(search, "::D");

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
        var results = await SearchAsync(search, ":fire");

        Assert.Equal(2, results.Count);
        Assert.Equal("🔥", results[0].Icon); // exact match wins
    }

    [Fact]
    public async Task NonColonQuery_ReturnsNothing() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";

        var search = await BuildSearchWithCache(json);
        var results = await SearchAsync(search, "grinning");

        Assert.Empty(results);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_HasCorrectShape() {
        var json = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";

        var search = await BuildSearchWithCache(json);
        var results = await SearchAsync(search, ":");

        var item = Assert.Single(results);
        Assert.Equal("😀", item.Icon);
        Assert.Equal("grinning face", item.Title);
        Assert.Equal("Emoji", item.Category);
        Assert.Equal(3.5, item.Score);
        Assert.True(item.PasteAfterActivate);
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

        var results = await SearchAsync(search, ":");
        var item = Assert.Single(results);
        Assert.NotNull(item.OnActivate);
        item.OnActivate();

        Assert.Equal("😀", copied);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Emoji;

namespace Yottacast.Core.Tests.Search;

public class EmojiDataLoaderTests {

    // ── ParseRawJson ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseRawJson_ReturnsEntries_ForValidInput() {
        var json = """
        [
          {
            "name": "THUMBS UP SIGN",
            "unified": "1F44D",
            "short_names": ["thumbsup", "+1"],
            "texts": [],
            "category": "People & Body",
            "sort_order": 10
          }
        ]
        """;

        var entries = EmojiDataLoader.ParseRawJson(json);

        var entry = Assert.Single(entries);
        Assert.Equal("thumbs up sign", entry.Name);
        Assert.Equal("People & Body", entry.Category);
        Assert.Equal(10, entry.SortOrder);
        Assert.Contains("thumbsup", entry.Keywords);
        Assert.Contains("+1", entry.Keywords);
        Assert.NotEmpty(entry.Char);
    }

    [Fact]
    public void ParseRawJson_SkipsObsoletedEmojis() {
        var json = """
        [
          {
            "name": "THUMBS UP SIGN",
            "unified": "1F44D",
            "short_names": ["thumbsup"],
            "texts": [],
            "category": "People & Body",
            "sort_order": 10
          },
          {
            "name": "OLD EMOJI",
            "unified": "1F600",
            "short_names": ["old"],
            "texts": [],
            "category": "Smileys & Emotion",
            "sort_order": 1,
            "obsoleted_by": "1F601"
          }
        ]
        """;

        var entries = EmojiDataLoader.ParseRawJson(json);

        Assert.Single(entries);
        Assert.Equal("thumbs up sign", entries[0].Name);
    }

    [Fact]
    public void ParseRawJson_IncludesTextsInKeywords() {
        var json = """
        [
          {
            "name": "SMILING FACE",
            "unified": "1F600",
            "short_names": ["grinning"],
            "texts": [":D", ":-)"],
            "category": "Smileys & Emotion",
            "sort_order": 1
          }
        ]
        """;

        var entries = EmojiDataLoader.ParseRawJson(json);

        var entry = Assert.Single(entries);
        Assert.Contains(":D", entry.Keywords);
        Assert.Contains(":-)", entry.Keywords);
        Assert.Contains("grinning", entry.Keywords);
    }

    [Fact]
    public void ParseRawJson_HandlesMultiCodepointEmoji() {
        // Family emoji: man + ZWJ + woman + ZWJ + girl
        var json = """
        [
          {
            "name": "FAMILY: MAN, WOMAN, GIRL",
            "unified": "1F468-200D-1F469-200D-1F467",
            "short_names": ["family_man_woman_girl"],
            "texts": [],
            "category": "People & Body",
            "sort_order": 500
          }
        ]
        """;

        var entries = EmojiDataLoader.ParseRawJson(json);

        var entry = Assert.Single(entries);
        Assert.NotEmpty(entry.Char);
    }

    [Fact]
    public void ParseRawJson_IncludesFE0FVariationSelector() {
        // Emoji with FE0F variation selector (emoji presentation)
        var json = """
        [
          {
            "name": "WHITE UP POINTING INDEX",
            "unified": "261D-FE0F",
            "short_names": ["point_up"],
            "texts": [],
            "category": "People & Body",
            "sort_order": 170
          }
        ]
        """;

        var entries = EmojiDataLoader.ParseRawJson(json);

        var entry = Assert.Single(entries);
        // The char should contain 2 chars: the emoji + FE0F
        Assert.Equal(2, entry.Char.Length);
    }

    // ── ParseCompactCache ─────────────────────────────────────────────────────

    [Fact]
    public void ParseCompactCache_RoundTripsData() {
        var json = """[["👍","thumbs up sign",["thumbsup","+1"],"People & Body",10]]""";

        var entries = EmojiDataLoader.ParseCompactCache(json);

        var entry = Assert.Single(entries);
        Assert.Equal("👍", entry.Char);
        Assert.Equal("thumbs up sign", entry.Name);
        Assert.Equal(new[] { "thumbsup", "+1" }, entry.Keywords);
        Assert.Equal("People & Body", entry.Category);
        Assert.Equal(10, entry.SortOrder);
    }

    // ── LoadAsync with cache ──────────────────────────────────────────────────

    private static EmojiDataLoader Loader() => new(NullLogger<EmojiDataLoader>.Instance);

    [Fact]
    public async Task LoadAsync_ReturnsList_WhenCacheIsValid() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try {
            var cacheJson = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
            await File.WriteAllTextAsync(Path.Combine(dir, "emoji-cache.json"), cacheJson);

            var entries = await Loader().LoadAsync(dir);

            var entry = Assert.Single(entries);
            Assert.Equal("😀", entry.Char);
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesCacheRegardlessOfAge() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try {
            var cacheJson = """[["😀","grinning face",["grinning"],"Smileys & Emotion",1]]""";
            var cachePath = Path.Combine(dir, "emoji-cache.json");
            await File.WriteAllTextAsync(cachePath, cacheJson);
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow - TimeSpan.FromDays(365));

            var entries = await Loader().LoadAsync(dir);

            Assert.Single(entries); // loaded from cache, no TTL check
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_FallsBackToEmbeddedResource_WhenNoCacheExists() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        // Do NOT create the directory — no cache can exist
        try {
            var entries = await Loader().LoadAsync(dir);

            // The embedded resource has 1600+ emojis
            Assert.NotEmpty(entries);
        } finally {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

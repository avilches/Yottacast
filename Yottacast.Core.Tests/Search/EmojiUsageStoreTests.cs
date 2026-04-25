using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Emoji;

namespace Yottacast.Core.Tests.Search;

public class EmojiUsageStoreTests {

    private static string TempFile() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "emoji-usage.json");
    }

    private static EmojiUsageStore CreateStore(string filePath) =>
        new(filePath, NullLogger<EmojiUsageStore>.Instance);

    // ── Favorites ────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleFavorite_AddsAndRemoves() {
        var store = CreateStore(TempFile());
        Assert.False(store.IsFavorite("😀"));

        store.ToggleFavorite("😀");
        Assert.True(store.IsFavorite("😀"));
        Assert.Single(store.Favorites);

        store.ToggleFavorite("😀");
        Assert.False(store.IsFavorite("😀"));
        Assert.Empty(store.Favorites);
    }

    [Fact]
    public void ToggleFavorite_PreservesOrder() {
        var store = CreateStore(TempFile());
        store.ToggleFavorite("🔥");
        store.ToggleFavorite("❤️");
        store.ToggleFavorite("👍");

        Assert.Equal(["🔥", "❤️", "👍"], store.Favorites);
    }

    [Fact]
    public void ToggleFavorite_EvictsLeastUsed_WhenAtMaxCapacity() {
        var store = CreateStore(TempFile());
        store.ToggleFavorite("A");
        store.ToggleFavorite("B");
        store.ToggleFavorite("C");
        store.ToggleFavorite("D"); // 4 favorites, at cap

        // A has 5 uses, B has 1, C has 3, D has 0
        for (int i = 0; i < 5; i++) store.RecordUsage("A");
        store.RecordUsage("B");
        for (int i = 0; i < 3; i++) store.RecordUsage("C");

        store.ToggleFavorite("E"); // should evict D (0 uses, least used)
        Assert.Equal(["A", "B", "C", "E"], store.Favorites);
        Assert.False(store.IsFavorite("D"));
        Assert.True(store.IsFavorite("E"));
    }

    [Fact]
    public void ToggleFavorite_EvictsFirst_WhenAllSameUsage() {
        var store = CreateStore(TempFile());
        store.ToggleFavorite("A");
        store.ToggleFavorite("B");
        store.ToggleFavorite("C");
        store.ToggleFavorite("D"); // 4 favorites, all with 0 uses

        store.ToggleFavorite("E"); // should evict A (first in list)
        Assert.Equal(["B", "C", "D", "E"], store.Favorites);
        Assert.False(store.IsFavorite("A"));
        Assert.True(store.IsFavorite("E"));
    }

    // ── Usage ────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordUsage_IncrementsCount() {
        var store = CreateStore(TempFile());
        store.RecordUsage("😀");
        store.RecordUsage("😀");
        store.RecordUsage("🔥");

        var mostUsed = store.GetMostUsed(10);
        Assert.Equal("😀", mostUsed[0]); // 2 uses
        Assert.Equal("🔥", mostUsed[1]); // 1 use
    }

    [Fact]
    public void GetMostUsed_ExcludesFavorites() {
        var store = CreateStore(TempFile());
        store.RecordUsage("😀");
        store.RecordUsage("😀");
        store.RecordUsage("🔥");
        store.ToggleFavorite("😀");

        var mostUsed = store.GetMostUsed(10);
        Assert.Single(mostUsed);
        Assert.Equal("🔥", mostUsed[0]);
    }

    [Fact]
    public void GetMostUsed_RespectsMaxLimit() {
        var store = CreateStore(TempFile());
        store.RecordUsage("😀");
        store.RecordUsage("🔥");
        store.RecordUsage("👍");

        var mostUsed = store.GetMostUsed(2);
        Assert.Equal(2, mostUsed.Count);
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndReload_PreservesData() {
        var path = TempFile();

        var store1 = CreateStore(path);
        store1.ToggleFavorite("❤️");
        store1.ToggleFavorite("🔥");
        store1.RecordUsage("👍");
        store1.RecordUsage("👍");
        store1.RecordUsage("😀");

        var store2 = CreateStore(path);
        await store2.LoadAsync();

        Assert.Equal(["❤️", "🔥"], store2.Favorites);
        Assert.True(store2.IsFavorite("❤️"));
        Assert.True(store2.IsFavorite("🔥"));

        var mostUsed = store2.GetMostUsed(10);
        Assert.Equal("👍", mostUsed[0]); // 2 uses
        Assert.Equal("😀", mostUsed[1]); // 1 use
    }

    [Fact]
    public async Task LoadAsync_MissingFile_StartsEmpty() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.json");
        var store = CreateStore(path);
        await store.LoadAsync();

        Assert.Empty(store.Favorites);
        Assert.Empty(store.GetMostUsed(10));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_StartsEmpty() {
        var path = TempFile();
        await File.WriteAllTextAsync(path, "not valid json {{{");

        var store = CreateStore(path);
        await store.LoadAsync();

        Assert.Empty(store.Favorites);
        Assert.Empty(store.GetMostUsed(10));
    }
}

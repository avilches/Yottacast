using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Clipboard;

namespace Yottacast.Core.Tests.Search;

public class ClipboardHistoryStoreTests
{
    private static ClipboardHistoryStore BuildStore(string? filePath = null)
    {
        filePath ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        return new ClipboardHistoryStore(filePath, NullLogger<ClipboardHistoryStore>.Instance);
    }

    [Fact]
    public void Add_NewText_InsertsAtFront()
    {
        var store = BuildStore();
        store.Add("hello");
        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal("hello", entries[0].Text);
        Assert.Equal(0, entries[0].UsageCount);
    }

    [Fact]
    public void Add_MultipleTexts_MostRecentFirst()
    {
        var store = BuildStore();
        store.Add("first");
        store.Add("second");
        var entries = store.GetAll();
        Assert.Equal("second", entries[0].Text);
        Assert.Equal("first", entries[1].Text);
    }

    [Fact]
    public void Add_DuplicateText_DeduplicatesAndMovesToFront()
    {
        var store = BuildStore();
        store.Add("hello");
        store.Add("world");
        store.Add("hello"); // duplicate — should move to front
        var entries = store.GetAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal("hello", entries[0].Text);
        Assert.Equal("world", entries[1].Text);
    }

    [Fact]
    public void Add_DuplicateText_UpdatesCopiedAt()
    {
        var store = BuildStore();
        store.Add("hello");
        var firstCopied = store.GetAll()[0].CopiedAt;
        System.Threading.Thread.Sleep(10);
        store.Add("hello");
        var secondCopied = store.GetAll()[0].CopiedAt;
        Assert.True(secondCopied >= firstCopied);
    }

    [Fact]
    public void Add_ExceedsMaxEntries_TrimsOldest()
    {
        var store = BuildStore();
        for (int i = 0; i < 5; i++)
            store.Add($"entry-{i}");
        store.MaxEntries = 3;
        store.Add("new");
        Assert.Equal(3, store.GetAll().Count);
        Assert.Equal("new", store.GetAll()[0].Text);
    }

    [Fact]
    public void Add_EntryOlderThanMaxDays_IsDiscarded()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var callCount = 0;
        DateTimeOffset[] times = [
            baseTime.AddDays(-35), // primera entrada, ya "vieja"
            baseTime,              // segunda entrada, nueva — trigger de limpieza
        ];
        var store = new ClipboardHistoryStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            NullLogger<ClipboardHistoryStore>.Instance,
            clock: () => times[Math.Min(callCount++, times.Length - 1)]);
        store.MaxDays = 30;
        store.Add("old");   // CopiedAt = 35 días atrás
        store.Add("new");   // CopiedAt = ahora, trigger ApplyLimits
        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal("new", entries[0].Text);
    }

    [Fact]
    public void Remove_ExistingText_RemovesEntry()
    {
        var store = BuildStore();
        store.Add("hello");
        store.Add("world");
        store.Remove("hello");
        var entries = store.GetAll();
        Assert.Single(entries);
        Assert.Equal("world", entries[0].Text);
    }

    [Fact]
    public void Remove_NonExistingText_NoOp()
    {
        var store = BuildStore();
        store.Add("hello");
        store.Remove("nope");
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void RecordUsage_IncrementsCountAndUpdatesLastUsed()
    {
        var store = BuildStore();
        store.Add("hello");
        var before = store.GetAll()[0].LastUsedAt;
        System.Threading.Thread.Sleep(10);
        store.RecordUsage("hello");
        var entry = store.GetAll()[0];
        Assert.Equal(1, entry.UsageCount);
        Assert.True(entry.LastUsedAt >= before);
    }

    [Fact]
    public void RecordUsage_NonExisting_NoOp()
    {
        var store = BuildStore();
        store.RecordUsage("ghost");  // should not throw
    }

    [Fact]
    public void EntriesChanged_FiredAfterAdd()
    {
        var store = BuildStore();
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.Add("hello");
        Assert.True(fired);
    }

    [Fact]
    public void EntriesChanged_FiredAfterRemove()
    {
        var store = BuildStore();
        store.Add("hello");
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.Remove("hello");
        Assert.True(fired);
    }

    [Fact]
    public void EntriesChanged_FiredAfterRecordUsage()
    {
        var store = BuildStore();
        store.Add("hello");
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.RecordUsage("hello");
        Assert.True(fired);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store1 = new ClipboardHistoryStore(file, NullLogger<ClipboardHistoryStore>.Instance);
        store1.Add("hello");
        store1.Add("world");
        store1.RecordUsage("hello");
        await store1.FlushAsync();

        var store2 = new ClipboardHistoryStore(file, NullLogger<ClipboardHistoryStore>.Instance);
        await store2.LoadAsync();
        var entries = store2.GetAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal("world", entries[0].Text);
        Assert.Equal("hello", entries[1].Text);
        Assert.Equal(1, entries[1].UsageCount);
    }

    [Fact]
    public void EntriesChanged_NotFiredAfterRemoveNonExisting()
    {
        var store = BuildStore();
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.Remove("ghost");
        Assert.False(fired);
    }

    [Fact]
    public void EntriesChanged_NotFiredAfterRecordUsageNonExisting()
    {
        var store = BuildStore();
        var fired = false;
        store.EntriesChanged += () => fired = true;
        store.RecordUsage("ghost");
        Assert.False(fired);
    }
}

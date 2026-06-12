using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Clipboard;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

public class ClipboardHistorySearchTests
{
    private static (ClipboardHistorySearch search, ClipboardHistoryStore store, UserSettings settings) Build(
        SearchSourceVisibility visibility = SearchSourceVisibility.ModeOnly,
        bool historyEnabled = true)
    {
        var platform = new FakePlatformProvider([]);
        var settings = UserSettings.Load(platform);
        settings.ClipboardHistoryEnabled = historyEnabled;
        settings.ClipboardSearchVisibility = visibility;
        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new ClipboardHistoryStore(filePath, NullLogger<ClipboardHistoryStore>.Instance);
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var search = new ClipboardHistorySearch(settings, store, clipboard, NullLogger<ClipboardHistorySearch>.Instance);
        return (search, store, settings);
    }

    [Fact]
    public void IsActiveIn_ModeOnly_ActiveInClipboardOnly()
    {
        var (search, _, _) = Build(SearchSourceVisibility.ModeOnly);
        Assert.True(search.IsActiveIn(SearchMode.Clipboard));
        Assert.False(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Files));
    }

    [Fact]
    public void IsActiveIn_Always_ActiveInAllOnly()
    {
        var (search, _, _) = Build(SearchSourceVisibility.Always);
        Assert.True(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Clipboard));
        Assert.False(search.IsActiveIn(SearchMode.Files));
    }

    [Fact]
    public void IsActiveIn_Disabled_NeverActive()
    {
        var (search, _, _) = Build(SearchSourceVisibility.Disabled);
        Assert.False(search.IsActiveIn(SearchMode.All));
        Assert.False(search.IsActiveIn(SearchMode.Clipboard));
    }

    [Fact]
    public void Search_HistoryDisabled_ReturnsEmpty()
    {
        var (search, store, _) = Build(historyEnabled: false);
        store.Add("hello");
        Assert.Empty(search.Search("", 10));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsMostRecentFirst()
    {
        var (search, store, _) = Build();
        store.Add("first");
        store.Add("second");
        var results = search.Search("", 10);
        Assert.Equal(2, results.Count);
        Assert.Contains("second", results[0].Title);
        Assert.Contains("first", results[1].Title);
    }

    [Fact]
    public void Search_EmptyQuery_RespectsLimit()
    {
        var (search, store, _) = Build();
        for (int i = 0; i < 5; i++) store.Add($"entry-{i}");
        var results = search.Search("", 3);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void Search_Query_FiltersContains()
    {
        var (search, store, _) = Build();
        store.Add("hello world");
        store.Add("goodbye");
        var results = search.Search("world", 10);
        Assert.Single(results);
        Assert.Contains("hello world", results[0].Title);
    }

    [Fact]
    public void Search_Query_CaseInsensitive()
    {
        var (search, store, _) = Build();
        store.Add("Hello World");
        var results = search.Search("hello", 10);
        Assert.Single(results);
    }

    [Fact]
    public void Score_ExactMatch_HigherThanStartsWith()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        store.Add("hello world");
        var results = search.Search("hello", 10);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("hello", results[0].Title);
    }

    [Fact]
    public void Score_StartsWith_HigherThanContains()
    {
        var (search, store, _) = Build();
        store.Add("world hello");
        store.Add("hello world");
        var results = search.Search("hello", 10);
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("hello world", results[0].Title);
    }

    [Fact]
    public void Score_UsageBonus_IncreasesScore()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var scoresBefore = search.Search("hello", 10).Select(r => r.Score).ToList();
        store.RecordUsage("hello");
        store.RecordUsage("hello");
        var scoresAfter = search.Search("hello", 10).Select(r => r.Score).ToList();
        Assert.True(scoresAfter[0] > scoresBefore[0]);
    }

    [Fact]
    public void Result_HasPasteAction_WithEnterHotkey()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var paste = result.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
        Assert.NotNull(paste);
        Assert.True(paste.PasteAfterClose);
        Assert.True(paste.ClosesWindow);
    }

    [Fact]
    public void Result_HasDeleteAction_WithDeleteHotkey()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var delete = result.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Delete);
        Assert.NotNull(delete);
        Assert.False(delete.ClosesWindow);
    }

    [Fact]
    public void DeleteAction_Execute_RemovesFromStore()
    {
        var (search, store, _) = Build();
        store.Add("hello");
        var result = search.Search("hello", 10).First();
        var delete = result.Actions.First(a => a.Hotkey == ActionHotkey.Delete);
        delete.Execute();
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void ResultChanged_FiredWhenStoreChanges()
    {
        var (search, store, _) = Build();
        search.Start();
        var fired = false;
        search.ResultChanged += () => fired = true;
        store.Add("hello");
        Assert.True(fired);
        search.Stop();
    }

    [Fact]
    public void Result_MultilineText_NewlinesReplacedInTitle()
    {
        var (search, store, _) = Build();
        store.Add("line1\nline2\nline3");
        var result = search.Search("line1", 10).First();
        Assert.DoesNotContain("\n", result.Title);
    }

    [Fact]
    public void Result_LongText_TruncatedTo120Chars()
    {
        var (search, store, _) = Build();
        var longText = new string('a', 200);
        store.Add(longText);
        var result = search.Search(new string('a', 5), 10).First();
        Assert.True(result.Title.Length <= 122);
    }
}

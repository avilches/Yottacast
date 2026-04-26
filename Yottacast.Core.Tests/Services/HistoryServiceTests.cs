using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Services;

public class HistoryServiceTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _historyFile;
    private readonly UserSettings _settings;

    public HistoryServiceTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastHistoryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _historyFile = Path.Combine(_tempDir, "history.json");
        _settings = UserSettings.Load(
            new MinimalPlatform(),
            settingsPath: Path.Combine(_tempDir, "settings.json"));
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private HistoryService MakeService() =>
        new(_settings, NullLogger<HistoryService>.Instance, _historyFile);

    [Fact]
    public void Add_StoresEntryWithCorrectFields() {
        var svc = MakeService();
        svc.Add("hello world", "WebSearch");
        Assert.Single(svc.Entries);
        Assert.Equal("hello world", svc.Entries[0].Query);
        Assert.Equal("WebSearch", svc.Entries[0].ActionName);
        Assert.True(svc.Entries[0].Timestamp <= DateTime.Now);
        Assert.True(svc.Entries[0].Timestamp >= DateTime.Now.AddSeconds(-5));
    }

    [Fact]
    public void Add_PersistsToDisk_AndLoadsBack() {
        var svc = MakeService();
        svc.Add("hello", null);
        var svc2 = MakeService();
        Assert.Single(svc2.Entries);
        Assert.Equal("hello", svc2.Entries[0].Query);
        Assert.Null(svc2.Entries[0].ActionName);
    }

    [Fact]
    public void Add_TrimsOldestWhenOverLimit() {
        _settings.HistoryMaxItems = 3;
        var svc = MakeService();
        svc.Add("q1", null);
        svc.Add("q2", null);
        svc.Add("q3", null);
        svc.Add("q4", null);
        Assert.Equal(3, svc.Entries.Count);
        Assert.Equal("q2", svc.Entries[0].Query);
        Assert.Equal("q4", svc.Entries[2].Query);
    }

    [Fact]
    public void Add_DoesNothingWhenHistoryDisabled() {
        _settings.EnableHistory = false;
        var svc = MakeService();
        svc.Add("hello", null);
        Assert.Empty(svc.Entries);
    }

    [Fact]
    public void Add_IgnoresWhitespaceQuery() {
        var svc = MakeService();
        svc.Add("   ", null);
        Assert.Empty(svc.Entries);
    }

    [Fact]
    public void Clear_RemovesAllEntriesAndPersists() {
        var svc = MakeService();
        svc.Add("a", null);
        svc.Add("b", null);
        svc.Clear();
        Assert.Empty(svc.Entries);
        var svc2 = MakeService();
        Assert.Empty(svc2.Entries);
    }

    [Fact]
    public void Changed_FiredOnAdd() {
        var svc = MakeService();
        bool fired = false;
        svc.Changed += () => fired = true;
        svc.Add("test", null);
        Assert.True(fired);
    }

    [Fact]
    public void Changed_FiredOnClear() {
        var svc = MakeService();
        svc.Add("test", null);
        bool fired = false;
        svc.Changed += () => fired = true;
        svc.Clear();
        Assert.True(fired);
    }

    [Fact]
    public void Load_HandlesNonExistentFile() {
        var svc = MakeService(); // no file exists yet
        Assert.Empty(svc.Entries);
    }

    [Fact]
    public void Add_MultipleEntries_OrderIsChronological() {
        var svc = MakeService();
        svc.Add("first", null);
        svc.Add("second", null);
        Assert.Equal("first", svc.Entries[0].Query);
        Assert.Equal("second", svc.Entries[1].Query);
    }
}

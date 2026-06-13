using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Services;

public class LaunchHistoryTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _historyFile;

    public LaunchHistoryTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"YottacastLaunchHistoryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _historyFile = Path.Combine(_tempDir, "launch-history.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private LaunchHistory Build(Func<DateTime>? clock = null) =>
        new(_historyFile, NullLogger<LaunchHistory>.Instance, clock);

    private static ResultItemViewModel ItemWith(string? itemPath) =>
        new() { Title = "Test", ItemPath = itemPath };

    // ── BonusFor — no match ───────────────────────────────────────────────────

    [Fact]
    public void BonusFor_UnknownPath_ReturnsZero() {
        var h = Build();
        Assert.Equal(0, h.BonusFor(ItemWith("/Applications/Safari.app")));
    }

    [Fact]
    public void BonusFor_ItemWithNullPath_ReturnsZero() {
        var h = Build();
        h.Record("/Applications/Safari.app");
        // Item has no ItemPath — should not receive a bonus
        Assert.Equal(0, h.BonusFor(ItemWith(null)));
    }

    [Fact]
    public void BonusFor_NonResultItemViewModel_ReturnsZero() {
        var h = Build();
        // Calculator/Emoji don't have ItemPath — use a base-class item
        var calcItem = new CalculatorResultItemViewModel { Title = "4", Score = 7 };
        Assert.Equal(0, h.BonusFor(calcItem));
    }

    // ── BonusFor — after recording ─────────────────────────────────────────────

    [Fact]
    public void BonusFor_AfterRecord_ReturnsPositive() {
        var now = DateTime.UtcNow;
        var h = Build(clock: () => now);
        h.Record("/Applications/Safari.app");

        var bonus = h.BonusFor(ItemWith("/Applications/Safari.app"));
        Assert.True(bonus > 0, $"Expected positive bonus but got {bonus}");
    }

    [Fact]
    public void BonusFor_AfterMoreRecords_HigherBonus() {
        var now = DateTime.UtcNow;
        var h = Build(clock: () => now);
        h.Record("/Applications/Safari.app");
        var bonusAfterOne = h.BonusFor(ItemWith("/Applications/Safari.app"));

        for (var i = 0; i < 9; i++) h.Record("/Applications/Safari.app");
        var bonusAfterTen = h.BonusFor(ItemWith("/Applications/Safari.app"));

        Assert.True(bonusAfterTen > bonusAfterOne,
            $"Expected bonus to grow with more records: {bonusAfterOne} → {bonusAfterTen}");
    }

    [Fact]
    public async Task BonusFor_OlderRecord_LowerBonus() {
        var past = DateTime.UtcNow.AddDays(-60);
        var present = DateTime.UtcNow;

        // Record the launch 60 days in the past (writes it to _historyFile).
        var hOld = Build(clock: () => past);
        hOld.Record("/Applications/Chrome.app");

        // Reload that same record with a present-day clock so the decay (60 days of age)
        // is actually applied. Without LoadAsync the store would be empty and the bonus 0,
        // making the comparison vacuous.
        var hOldPresent = new LaunchHistory(_historyFile, NullLogger<LaunchHistory>.Instance, () => present);
        await hOldPresent.LoadAsync();
        var oldBonus = hOldPresent.BonusFor(ItemWith("/Applications/Chrome.app"));

        // Record fresh in a new file with the same present clock (age ≈ 0, no decay).
        var freshFile = Path.Combine(_tempDir, "launch-history-fresh.json");
        var hFresh = new LaunchHistory(freshFile, NullLogger<LaunchHistory>.Instance, () => present);
        hFresh.Record("/Applications/Chrome.app");
        var freshBonus = hFresh.BonusFor(ItemWith("/Applications/Chrome.app"));

        // Both have exactly one launch, so the only difference is the recency decay:
        // the 60-day-old record must score strictly lower than the just-recorded one,
        // and the old bonus must still be positive (the entry was loaded, not missing).
        Assert.True(oldBonus > 0,
            $"Expected old record to be loaded with a positive (decayed) bonus but got {oldBonus}");
        Assert.True(oldBonus < freshBonus,
            $"Expected old record to have lower bonus ({oldBonus}) than fresh ({freshBonus})");
    }

    [Fact]
    public void BonusFor_NeverExceedsMaxBonus() {
        var now = DateTime.UtcNow;
        var h = Build(clock: () => now);
        for (var i = 0; i < 200; i++) h.Record("/Applications/Safari.app");

        var bonus = h.BonusFor(ItemWith("/Applications/Safari.app"));
        Assert.True(bonus <= AppDefaults.LaunchHistoryMaxBonus,
            $"Bonus {bonus} exceeds max {AppDefaults.LaunchHistoryMaxBonus}");
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_AfterSave_PreservesData() {
        var now = DateTime.UtcNow;
        var h1 = Build(clock: () => now);
        h1.Record("/Applications/Safari.app");
        h1.Record("/Applications/Safari.app");
        h1.Record("/Applications/Chrome.app");

        var h2 = Build(clock: () => now);
        await h2.LoadAsync();

        var safariBonus = h2.BonusFor(ItemWith("/Applications/Safari.app"));
        var chromeBonus = h2.BonusFor(ItemWith("/Applications/Chrome.app"));
        Assert.True(safariBonus > 0, "Safari bonus should be positive after reload");
        Assert.True(chromeBonus > 0, "Chrome bonus should be positive after reload");
        Assert.True(safariBonus > chromeBonus, "Safari (2 launches) should beat Chrome (1 launch)");
    }

    [Fact]
    public async Task LoadAsync_MissingFile_StartsEmpty() {
        var h = Build();
        await h.LoadAsync(); // file doesn't exist yet
        Assert.Equal(0, h.BonusFor(ItemWith("/Applications/Safari.app")));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_StartsEmpty() {
        await File.WriteAllTextAsync(_historyFile, "not valid json {{{");
        var h = Build();
        await h.LoadAsync();
        Assert.Equal(0, h.BonusFor(ItemWith("/Applications/Safari.app")));
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public void ConcurrentRecordAndScoring_DoesNotThrow() {
        // The scoring thread reads _data (BonusFor) in a tight loop while many threads call
        // Record concurrently. A plain Dictionary would throw or corrupt under this access
        // pattern; ConcurrentDictionary must let it run cleanly with no exception.
        var now = DateTime.UtcNow;
        var h = Build(clock: () => now);

        var paths = Enumerable.Range(0, 50)
            .Select(i => $"/Applications/App{i}.app")
            .ToArray();
        var items = paths.Select(ItemWith).ToArray();

        Exception? failure = null;
        using var stop = new CancellationTokenSource();

        // Reader thread: continuously score every item until the writers finish.
        var reader = Task.Run(() => {
            try {
                while (!stop.IsCancellationRequested) {
                    foreach (var item in items) {
                        _ = h.BonusFor(item);
                    }
                }
            } catch (Exception ex) {
                failure = ex;
            }
        });

        // Writers: hammer Record from many iterations across the same hot keys.
        try {
            Parallel.For(0, 500, i => {
                h.Record(paths[i % paths.Length]);
            });
        } catch (Exception ex) {
            failure = ex;
        } finally {
            stop.Cancel();
        }

        reader.Wait();

        Assert.Null(failure);
        // Sanity: scoring still works after the storm.
        Assert.True(h.BonusFor(items[0]) > 0);
    }
}

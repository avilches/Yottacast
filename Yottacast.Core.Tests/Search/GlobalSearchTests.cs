using System.Runtime.CompilerServices;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

// ── Fake IInstantSearchSource implementations ─────────────────────────────────

/// <summary>
/// Returns a single fixed snapshot synchronously.
/// </summary>
file sealed class StubInstantSource(IReadOnlyList<ResultItemViewModel> results, int limit = 100) : IInstantSearchSource {
    public bool WasSearched { get; private set; }
    public int Limit => limit;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        WasSearched = true;
        return results;
    }
}

// ── Fake IDeferredSearchSource implementations ────────────────────────────────

/// <summary>
/// Returns a single fixed snapshot asynchronously.
/// </summary>
file sealed class StubDeferredSource(IReadOnlyList<ResultItemViewModel> results) : IDeferredSearchSource {
    public bool WasSearched { get; private set; }

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        WasSearched = true;
        await Task.Yield();
        if (!ct.IsCancellationRequested)
            yield return results;
    }
}

/// <summary>
/// Emits multiple snapshots with a configurable delay between them.
/// </summary>
file sealed class MultiSnapshotDeferredSource(IReadOnlyList<IReadOnlyList<ResultItemViewModel>> snapshots, int delayMs = 0)
    : IDeferredSearchSource {

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var snap in snapshots) {
            if (delayMs > 0)
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            else
                await Task.Yield();
            yield return snap;
        }
    }
}

/// <summary>
/// Blocks indefinitely until the CancellationToken fires, then throws OperationCanceledException.
/// </summary>
file sealed class BlockingDeferredSource : IDeferredSearchSource {
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        yield break; // unreachable, but satisfies the compiler
    }
}

/// <summary>
/// Throws a non-OperationCanceledException after optionally emitting some snapshots.
/// </summary>
file sealed class ThrowingDeferredSource(IReadOnlyList<IReadOnlyList<ResultItemViewModel>> snapshots)
    : IDeferredSearchSource {

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var snap in snapshots) {
            await Task.Yield();
            yield return snap;
        }
        await Task.Yield();
        throw new InvalidOperationException("boom");
    }
}

/// <summary>
/// WhenReady() completes only after the supplied TaskCompletionSource is resolved.
/// </summary>
file sealed class DelayedReadyInstantSource(TaskCompletionSource tcs, IReadOnlyList<ResultItemViewModel> results)
    : IInstantSearchSource {

    public int Limit => 100;

    public void Start() { }
    public Task WhenReady() => tcs.Task;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) => results;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class ResultItem {
    public static ResultItemViewModel Make(string title, double score) =>
        new() { Title = title, Score = score };

    public static string TitleOf(BaseResultItemViewModel item) =>
        ((ResultItemViewModel)item).Title;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class GlobalSearchTests {

    // ── SearchInstant / SearchDeferredAsync routing ──────────────────────────

    [Fact]
    public void SearchInstant_OnlyCallsInstantSources() {
        var instant = new StubInstantSource([ResultItem.Make("A", 1.0)]);
        var deferred = new StubDeferredSource([ResultItem.Make("B", 1.0)]);
        var global = new GlobalSearch([instant], [deferred]);

        var (result, _, _) = global.SearchInstant("q", 10);

        Assert.True(instant.WasSearched, "instant source should have been searched");
        Assert.False(deferred.WasSearched, "deferred source must NOT be searched by SearchInstant");
        Assert.Single(result);
        Assert.Equal("A", ResultItem.TitleOf(result[0]));
    }

    [Fact]
    public async Task SearchDeferredAsync_OnlyCallsDeferredSources() {
        var instant = new StubInstantSource([ResultItem.Make("A", 1.0)]);
        var deferred = new StubDeferredSource([ResultItem.Make("B", 1.0)]);
        var global = new GlobalSearch([instant], [deferred]);

        var snapshots = new List<IReadOnlyList<BaseResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            snapshots.Add(snap);

        Assert.False(instant.WasSearched, "instant source must NOT be searched by SearchDeferredAsync");
        Assert.True(deferred.WasSearched, "deferred source should have been searched");
        Assert.Single(snapshots);
        Assert.Single(snapshots[0]);
        Assert.Equal("B", ResultItem.TitleOf(snapshots[0][0]));
    }

    // ── Merging and ordering ──────────────────────────────────────────────────

    [Fact]
    public void MultipleInstantSources_ResultsMergedAndSortedByScoreDescending() {
        var s1 = new StubInstantSource([
            ResultItem.Make("Low", 0.3),
            ResultItem.Make("High", 0.9),
        ]);
        var s2 = new StubInstantSource([
            ResultItem.Make("Mid", 0.6),
        ]);
        var global = new GlobalSearch([s1, s2], []);

        var (result, _, _) = global.SearchInstant("q", 10);

        Assert.Equal(3, result.Count);
        Assert.Equal("High", ResultItem.TitleOf(result[0]));
        Assert.Equal("Mid",  ResultItem.TitleOf(result[1]));
        Assert.Equal("Low",  ResultItem.TitleOf(result[2]));
    }

    [Fact]
    public async Task MultipleDeferredSources_ResultsMergedAndSortedByScoreDescending() {
        var s1 = new StubDeferredSource([
            ResultItem.Make("Low", 0.2),
            ResultItem.Make("Top", 1.0),
        ]);
        var s2 = new StubDeferredSource([
            ResultItem.Make("Mid", 0.5),
        ]);
        var global = new GlobalSearch([], [s1, s2]);

        IReadOnlyList<BaseResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            last = snap;

        Assert.Equal(3, last.Count);
        Assert.Equal("Top", ResultItem.TitleOf(last[0]));
        Assert.Equal("Mid", ResultItem.TitleOf(last[1]));
        Assert.Equal("Low", ResultItem.TitleOf(last[2]));
    }

    // ── Snapshot-slot replacement (not accumulation) ──────────────────────────

    [Fact]
    public async Task MultipleSnapshots_SlotReplacedNotAccumulated() {
        // First snapshot: ["A" score 0.8], second snapshot: ["B" score 0.9] (replaces first)
        var snap1 = new List<ResultItemViewModel> { ResultItem.Make("A", 0.8) };
        var snap2 = new List<ResultItemViewModel> { ResultItem.Make("B", 0.9) };
        var source = new MultiSnapshotDeferredSource([snap1, snap2]);
        var global = new GlobalSearch([], [source]);

        var allSnapshots = new List<IReadOnlyList<BaseResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            allSnapshots.Add(snap);

        // Should have received exactly 2 merged snapshots
        Assert.Equal(2, allSnapshots.Count);

        // First emission: only "A"
        Assert.Single(allSnapshots[0]);
        Assert.Equal("A", ResultItem.TitleOf(allSnapshots[0][0]));

        // Second emission: slot replaced → only "B", "A" is gone
        Assert.Single(allSnapshots[1]);
        Assert.Equal("B", ResultItem.TitleOf(allSnapshots[1][0]));
    }

    [Fact]
    public async Task TwoSources_SecondSourceSnapshotReplacesItsOwnSlot() {
        // Source 1 emits one snapshot; source 2 emits two snapshots replacing its slot.
        var s1 = new StubDeferredSource([ResultItem.Make("S1", 0.5)]);
        var snap2a = new List<ResultItemViewModel> { ResultItem.Make("S2v1", 0.3) };
        var snap2b = new List<ResultItemViewModel> { ResultItem.Make("S2v2", 0.7) };
        var s2 = new MultiSnapshotDeferredSource([snap2a, snap2b]);
        var global = new GlobalSearch([], [s1, s2]);

        IReadOnlyList<BaseResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            last = snap;

        // Final state: S1 (0.5) + S2v2 (0.7), sorted → S2v2, S1
        Assert.Equal(2, last.Count);
        Assert.Equal("S2v2", ResultItem.TitleOf(last[0]));
        Assert.Equal("S1",   ResultItem.TitleOf(last[1]));
        // S2v1 must NOT appear (it was replaced)
        Assert.DoesNotContain(last, r => r is ResultItemViewModel ri && ri.Title == "S2v1");
    }

    // ── Faulting sources ──────────────────────────────────────────────────────

    [Fact]
    public async Task ThrowingSource_DoesNotPropagateOrHang() {
        // A source that throws a non-OCE exception must not break the merge stream:
        // the stream completes cleanly and no exception bubbles to the consumer.
        var snap = new List<ResultItemViewModel> { ResultItem.Make("A", 0.5) };
        var source = new ThrowingDeferredSource([snap]);
        var global = new GlobalSearch([], [source]);

        var allSnapshots = new List<IReadOnlyList<BaseResultItemViewModel>>();
        await foreach (var s in global.SearchDeferredAsync("q", 10))
            allSnapshots.Add(s);

        // The snapshot emitted before the throw is still delivered.
        Assert.Single(allSnapshots);
        Assert.Single(allSnapshots[0]);
        Assert.Equal("A", ResultItem.TitleOf(allSnapshots[0][0]));
    }

    [Fact]
    public async Task ThrowingSource_OtherSourcesStillEmit() {
        // When one source faults, healthy sources still contribute their results.
        var throwing = new ThrowingDeferredSource([]);
        var healthy = new StubDeferredSource([ResultItem.Make("OK", 0.9)]);
        var global = new GlobalSearch([], [throwing, healthy]);

        IReadOnlyList<BaseResultItemViewModel> last = [];
        await foreach (var s in global.SearchDeferredAsync("q", 10))
            last = s;

        Assert.Single(last);
        Assert.Equal("OK", ResultItem.TitleOf(last[0]));
    }

    // ── Limit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Limit_EnforcedOnMergedResults() {
        var s1 = new StubDeferredSource([
            ResultItem.Make("A", 1.0),
            ResultItem.Make("B", 0.9),
            ResultItem.Make("C", 0.8),
        ]);
        var s2 = new StubDeferredSource([
            ResultItem.Make("D", 0.7),
            ResultItem.Make("E", 0.6),
        ]);
        var global = new GlobalSearch([], [s1, s2]);

        IReadOnlyList<BaseResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", limit: 3))
            last = snap;

        Assert.Equal(3, last.Count);
        Assert.Equal("A", ResultItem.TitleOf(last[0]));
        Assert.Equal("B", ResultItem.TitleOf(last[1]));
        Assert.Equal("C", ResultItem.TitleOf(last[2]));
    }

    [Fact]
    public void Limit_MergesAllSourcesWithoutGlobalCap() {
        // Each source uses its own Limit (AppDefaults.SearchSourceLimit = 10 by default).
        // There is no global cap: all items from all sources are returned, sorted by score.
        var s1 = new StubInstantSource([
            ResultItem.Make("A", 1.0),
            ResultItem.Make("B", 0.9),
            ResultItem.Make("C", 0.8),
        ]);
        var s2 = new StubInstantSource([
            ResultItem.Make("D", 0.7),
            ResultItem.Make("E", 0.6),
        ]);
        var global = new GlobalSearch([s1, s2], []);

        var (result, _, _) = global.SearchInstant("q", limit: 3);

        Assert.Equal(5, result.Count);
        Assert.Equal("A", ResultItem.TitleOf(result[0]));
        Assert.Equal("B", ResultItem.TitleOf(result[1]));
        Assert.Equal("C", ResultItem.TitleOf(result[2]));
        Assert.Equal("D", ResultItem.TitleOf(result[3]));
        Assert.Equal("E", ResultItem.TitleOf(result[4]));
    }

    // ── Empty query / no sources ──────────────────────────────────────────────

    [Fact]
    public async Task NoSources_ReturnsNoSnapshots() {
        var global = new GlobalSearch([], []);

        var (instantResult, _, _) = global.SearchInstant("q", 10);
        Assert.Empty(instantResult);

        var count = 0;
        await foreach (var _ in global.SearchDeferredAsync("q", 10))
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SourceReturnsEmptySnapshot_EmitsOneEmptySnapshot() {
        // A source that yields an empty list still causes one snapshot to be emitted
        // (the merged result of all slots, which is also empty).
        var source = new StubDeferredSource([]);
        var global = new GlobalSearch([], [source]);

        var snapshots = new List<IReadOnlyList<BaseResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            snapshots.Add(snap);

        Assert.Single(snapshots);
        Assert.Empty(snapshots[0]);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_StopsIteration() {
        var source = new BlockingDeferredSource();
        var global = new GlobalSearch([], [source]);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in global.SearchDeferredAsync("q", 10, ct: cts.Token))
                count++;
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Cancellation_AlreadyCancelled_ReturnsImmediately() {
        var source = new StubDeferredSource([ResultItem.Make("X", 1.0)]);
        var global = new GlobalSearch([], [source]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in global.SearchDeferredAsync("q", 10, ct: cts.Token))
                count++;
        });

        Assert.Equal(0, count);
    }

    // ── WhenReady ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenReady_CompletesWhenAllSourcesReady() {
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        var s1 = new DelayedReadyInstantSource(tcs1, []);
        var s2 = new DelayedReadyInstantSource(tcs2, []);
        var global = new GlobalSearch([s1, s2], []);

        var readyTask = global.WhenReady();

        // Neither has signaled yet
        Assert.False(readyTask.IsCompleted);

        tcs1.SetResult();
        await Task.Yield();
        Assert.False(readyTask.IsCompleted);

        tcs2.SetResult();
        await readyTask; // should complete now

        Assert.True(readyTask.IsCompleted);
    }

    [Fact]
    public async Task WhenReady_NoSources_CompletesImmediately() {
        var global = new GlobalSearch([], []);
        await global.WhenReady(); // must not hang
    }

    // ── SearchInstant / SearchDeferredAsync with no matching sources ──────────

    [Fact]
    public void SearchInstant_NoInstantSources_ReturnsEmpty() {
        var global = new GlobalSearch([], [new StubDeferredSource([ResultItem.Make("D", 1.0)])]);

        var (result, _, _) = global.SearchInstant("q", 10);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchDeferredAsync_NoDeferredSources_ReturnsNoSnapshots() {
        var global = new GlobalSearch([new StubInstantSource([ResultItem.Make("I", 1.0)])], []);

        var count = 0;
        await foreach (var _ in global.SearchDeferredAsync("q", 10))
            count++;

        Assert.Equal(0, count);
    }

    // ── Mixed instant/deferred sources in same GlobalSearch ───────────────────

    [Fact]
    public async Task MixedSources_InstantAndDeferredSearchReturnCorrectSubsets() {
        var instant = new StubInstantSource([ResultItem.Make("Instant", 0.8)]);
        var deferred = new StubDeferredSource([ResultItem.Make("Deferred", 0.9)]);
        var global = new GlobalSearch([instant], [deferred]);

        var (instantResult, _, _) = global.SearchInstant("q", 10);

        IReadOnlyList<BaseResultItemViewModel> deferredResult = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            deferredResult = snap;

        Assert.Single(instantResult);
        Assert.Equal("Instant", ResultItem.TitleOf(instantResult[0]));

        Assert.Single(deferredResult);
        Assert.Equal("Deferred", ResultItem.TitleOf(deferredResult[0]));
    }
}

// ── Filtrado por modo ────────────────────────────────────────────────────────

file sealed class ModeInstantSource(SearchMode mode, SearchSourceVisibility visibility)
    : IInstantSearchSource, ISearchModeSource {
    public bool WasSearched { get; private set; }
    public int Limit => 100;
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;
    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        WasSearched = true;
        return [new ResultItemViewModel { Title = $"result-{mode}", Score = 1.0 }];
    }
    public bool IsActiveIn(SearchMode m) => m switch {
        SearchMode.All   => visibility == SearchSourceVisibility.Always,
        var x when x == mode => visibility == SearchSourceVisibility.ModeOnly,
        _ => false,
    };
}

file sealed class ModeDeferred(SearchMode mode, SearchSourceVisibility visibility, IReadOnlyList<ResultItemViewModel> results)
    : IDeferredSearchSource, ISearchModeSource {
    public bool WasSearched { get; private set; }
    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;
    public bool IsActiveIn(SearchMode m) => m switch {
        SearchMode.All   => visibility == SearchSourceVisibility.Always,
        var x when x == mode => visibility == SearchSourceVisibility.ModeOnly,
        _ => false,
    };
    public async IAsyncEnumerable<IReadOnlyList<BaseResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        WasSearched = true;
        await Task.Yield();
        yield return results;
    }
}

public class GlobalSearchModeTests {

    [Fact]
    public void SearchInstant_AllMode_ExcludesModeOnlySources() {
        var always = new StubInstantSource([new ResultItemViewModel { Title = "always", Score = 1.0 }]);
        var modeOnly = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.ModeOnly);
        var gs = new GlobalSearch([always, modeOnly], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.All);

        Assert.Single(items);
        Assert.Equal("always", ((ResultItemViewModel)items[0]).Title);
        Assert.True(always.WasSearched);
        Assert.False(modeOnly.WasSearched);
    }

    [Fact]
    public void SearchInstant_FilesMode_OnlyIncludesModeOnlyFilesSource() {
        var always = new StubInstantSource([new ResultItemViewModel { Title = "always", Score = 1.0 }]);
        var modeOnly = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.ModeOnly);
        var gs = new GlobalSearch([always, modeOnly], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.Files);

        Assert.Single(items);
        Assert.Equal("result-Files", ((ResultItemViewModel)items[0]).Title);
        Assert.False(always.WasSearched);
        Assert.True(modeOnly.WasSearched);
    }

    [Fact]
    public void SearchInstant_AllMode_AlwaysSourceActive() {
        var always = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.Always);
        var gs = new GlobalSearch([always], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.All);

        Assert.Single(items);
        Assert.True(always.WasSearched);
    }

    [Fact]
    public async Task SearchDeferred_FilesMode_OnlyIncludesModeOnlyFilesSource() {
        var alwaysDeferred = new StubDeferredSource([new ResultItemViewModel { Title = "always-deferred", Score = 1.0 }]);
        var modeOnlyDeferred = new ModeDeferred(SearchMode.Files, SearchSourceVisibility.ModeOnly,
            [new ResultItemViewModel { Title = "files-deferred", Score = 1.0 }]);
        var gs = new GlobalSearch([], [alwaysDeferred, modeOnlyDeferred]);

        var results = new List<IReadOnlyList<BaseResultItemViewModel>>();
        await foreach (var snap in gs.SearchDeferredAsync("q", 10, SearchMode.Files))
            results.Add(snap);

        Assert.False(alwaysDeferred.WasSearched);
        Assert.True(modeOnlyDeferred.WasSearched);
        Assert.Single(results.Last());
        Assert.Equal("files-deferred", ((ResultItemViewModel)results.Last()[0]).Title);
    }

    [Fact]
    public void SearchInstant_FilesMode_ExcludesAlwaysSource() {
        var alwaysSource = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.Always);
        var modeOnlySource = new ModeInstantSource(SearchMode.Files, SearchSourceVisibility.ModeOnly);
        var gs = new GlobalSearch([alwaysSource, modeOnlySource], []);

        var (items, _, _) = gs.SearchInstant("q", 10, SearchMode.Files);

        Assert.False(alwaysSource.WasSearched);
        Assert.True(modeOnlySource.WasSearched);
        Assert.Single(items);
    }
}

// ── File-vs-app deduplication helpers ──────────────────────────────────────────

public class GlobalSearchDedupTests {

    private static ResultItemViewModel App(string title, string path) =>
        new() { Title = title, ItemPath = path, Category = "Application", Score = 4.0 };

    private static FileResultItemViewModel File(string title, string path) =>
        new() { Title = title, ItemPath = path, Category = "Documents", Score = 3.5 };

    [Fact]
    public void AppResultPaths_OnlyAppCategoryWithPath() {
        var paths = GlobalSearch.AppResultPaths([
            App("Safari", "/Applications/Safari.app"),
            File("Safari.app", "/Downloads/Safari.app"),
            App("Mail", "/Applications/Mail.app"),
        ]);
        Assert.Equal(new List<string> { "/Applications/Mail.app", "/Applications/Safari.app" },
            paths.OrderBy(x => x).ToList());
    }

    [Fact]
    public void RemoveFilesDuplicatingApps_RemovesFileWithSamePathAsApp() {
        var items = new List<BaseResultItemViewModel> {
            App("Safari", "/Applications/Safari.app"),
            File("Safari.app", "/Applications/Safari.app"), // same bundle → removed
            File("notes.txt", "/Desktop/notes.txt"),
        };
        var result = GlobalSearch.RemoveFilesDuplicatingApps(items, GlobalSearch.AppResultPaths(items));
        Assert.Equal(new List<string> { "Safari", "notes.txt" },
            result.OfType<ResultItemViewModel>().Select(x => x.Title).ToList());
    }

    [Fact]
    public void RemoveFilesDuplicatingApps_KeepsFileThatOnlySharesNameWithApp() {
        // The reported bug: Safari.txt shares the app's name but is a distinct file → must be kept.
        var items = new List<BaseResultItemViewModel> {
            App("Safari", "/Applications/Safari.app"),
            File("Safari.txt", "/Desktop/Safari.txt"),
        };
        var result = GlobalSearch.RemoveFilesDuplicatingApps(items, GlobalSearch.AppResultPaths(items));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void RemoveFilesDuplicatingApps_PathMatchIsCaseInsensitive() {
        var items = new List<BaseResultItemViewModel> {
            App("Safari", "/Applications/Safari.app"),
            File("Safari.app", "/APPLICATIONS/SAFARI.APP"),
        };
        var result = GlobalSearch.RemoveFilesDuplicatingApps(items, GlobalSearch.AppResultPaths(items));
        Assert.Single(result);
        Assert.IsType<ResultItemViewModel>(result[0]);
    }

    [Fact]
    public void RemoveFilesDuplicatingApps_NoApps_ReturnsInputUnchanged() {
        var items = new List<BaseResultItemViewModel> { File("Safari.app", "/Downloads/Safari.app") };
        var result = GlobalSearch.RemoveFilesDuplicatingApps(items, GlobalSearch.AppResultPaths(items));
        Assert.Same(items, result);
    }

    [Fact]
    public void DeduplicateFilesAgainstApps_PreservesOrder() {
        var items = new List<BaseResultItemViewModel> {
            App("Safari", "/Applications/Safari.app"),
            File("doc.pdf", "/Desktop/doc.pdf"),
            File("Safari.app", "/Applications/Safari.app"),
            App("Mail", "/Applications/Mail.app"),
        };
        var result = GlobalSearch.DeduplicateFilesAgainstApps(items);
        Assert.Equal(new List<string> { "Safari", "doc.pdf", "Mail" },
            result.OfType<ResultItemViewModel>().Select(x => x.Title).ToList());
    }
}

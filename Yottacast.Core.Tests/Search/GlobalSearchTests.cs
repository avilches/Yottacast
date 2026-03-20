using System.Runtime.CompilerServices;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

// ── Fake ISearchSource implementations ───────────────────────────────────────

/// <summary>
/// Returns a single fixed snapshot synchronously. Configurable IsInstant.
/// </summary>
file sealed class StubSource(bool isInstant, IReadOnlyList<ResultItemViewModel> results)
    : ISearchSource {

    public bool IsInstant => isInstant;
    public bool WasSearched { get; private set; }

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
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
file sealed class MultiSnapshotSource(bool isInstant, IReadOnlyList<IReadOnlyList<ResultItemViewModel>> snapshots, int delayMs = 0)
    : ISearchSource {

    public bool IsInstant => isInstant;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
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
file sealed class BlockingSource(bool isInstant) : ISearchSource {
    public bool IsInstant => isInstant;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        yield break; // unreachable, but satisfies the compiler
    }
}

/// <summary>
/// WhenReady() completes only after the supplied TaskCompletionSource is resolved.
/// </summary>
file sealed class DelayedReadySource(TaskCompletionSource tcs, IReadOnlyList<ResultItemViewModel> results)
    : ISearchSource {

    public bool IsInstant => false;

    public void Start() { }
    public Task WhenReady() => tcs.Task;
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.Yield();
        yield return results;
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class ResultItem {
    public static ResultItemViewModel Make(string title, double score) =>
        new() { Title = title, Score = score };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class GlobalSearchTests {

    // ── SearchInstantAsync / SearchDeferredAsync routing ─────────────────────

    [Fact]
    public async Task SearchInstantAsync_OnlyCallsInstantSources() {
        var instant = new StubSource(isInstant: true, [ResultItem.Make("A", 1.0)]);
        var deferred = new StubSource(isInstant: false, [ResultItem.Make("B", 1.0)]);
        var global = new GlobalSearch([instant, deferred]);

        var snapshots = new List<IReadOnlyList<ResultItemViewModel>>();
        await foreach (var snap in global.SearchInstantAsync("q", 10))
            snapshots.Add(snap);

        Assert.True(instant.WasSearched, "instant source should have been searched");
        Assert.False(deferred.WasSearched, "deferred source must NOT be searched by SearchInstantAsync");
        Assert.Single(snapshots);
        Assert.Single(snapshots[0]);
        Assert.Equal("A", snapshots[0][0].Title);
    }

    [Fact]
    public async Task SearchDeferredAsync_OnlyCallsDeferredSources() {
        var instant = new StubSource(isInstant: true, [ResultItem.Make("A", 1.0)]);
        var deferred = new StubSource(isInstant: false, [ResultItem.Make("B", 1.0)]);
        var global = new GlobalSearch([instant, deferred]);

        var snapshots = new List<IReadOnlyList<ResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            snapshots.Add(snap);

        Assert.False(instant.WasSearched, "instant source must NOT be searched by SearchDeferredAsync");
        Assert.True(deferred.WasSearched, "deferred source should have been searched");
        Assert.Single(snapshots);
        Assert.Single(snapshots[0]);
        Assert.Equal("B", snapshots[0][0].Title);
    }

    // ── Merging and ordering ──────────────────────────────────────────────────

    [Fact]
    public async Task MultipleInstantSources_ResultsMergedAndSortedByScoreDescending() {
        var s1 = new StubSource(isInstant: true, [
            ResultItem.Make("Low", 0.3),
            ResultItem.Make("High", 0.9),
        ]);
        var s2 = new StubSource(isInstant: true, [
            ResultItem.Make("Mid", 0.6),
        ]);
        var global = new GlobalSearch([s1, s2]);

        IReadOnlyList<ResultItemViewModel> last = [];
        await foreach (var snap in global.SearchInstantAsync("q", 10))
            last = snap;

        Assert.Equal(3, last.Count);
        Assert.Equal("High", last[0].Title);
        Assert.Equal("Mid", last[1].Title);
        Assert.Equal("Low", last[2].Title);
    }

    [Fact]
    public async Task MultipleDeferredSources_ResultsMergedAndSortedByScoreDescending() {
        var s1 = new StubSource(isInstant: false, [
            ResultItem.Make("Low", 0.2),
            ResultItem.Make("Top", 1.0),
        ]);
        var s2 = new StubSource(isInstant: false, [
            ResultItem.Make("Mid", 0.5),
        ]);
        var global = new GlobalSearch([s1, s2]);

        IReadOnlyList<ResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            last = snap;

        Assert.Equal(3, last.Count);
        Assert.Equal("Top", last[0].Title);
        Assert.Equal("Mid", last[1].Title);
        Assert.Equal("Low", last[2].Title);
    }

    // ── Snapshot-slot replacement (not accumulation) ──────────────────────────

    [Fact]
    public async Task MultipleSnapshots_SlotReplacedNotAccumulated() {
        // First snapshot: ["A" score 0.8], second snapshot: ["B" score 0.9] (replaces first)
        var snap1 = new List<ResultItemViewModel> { ResultItem.Make("A", 0.8) };
        var snap2 = new List<ResultItemViewModel> { ResultItem.Make("B", 0.9) };
        var source = new MultiSnapshotSource(isInstant: false, [snap1, snap2]);
        var global = new GlobalSearch([source]);

        var allSnapshots = new List<IReadOnlyList<ResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            allSnapshots.Add(snap);

        // Should have received exactly 2 merged snapshots
        Assert.Equal(2, allSnapshots.Count);

        // First emission: only "A"
        Assert.Single(allSnapshots[0]);
        Assert.Equal("A", allSnapshots[0][0].Title);

        // Second emission: slot replaced → only "B", "A" is gone
        Assert.Single(allSnapshots[1]);
        Assert.Equal("B", allSnapshots[1][0].Title);
    }

    [Fact]
    public async Task TwoSources_SecondSourceSnapshotReplacesItsOwnSlot() {
        // Source 1 emits one snapshot; source 2 emits two snapshots replacing its slot.
        var s1 = new StubSource(isInstant: false, [ResultItem.Make("S1", 0.5)]);
        var snap2a = new List<ResultItemViewModel> { ResultItem.Make("S2v1", 0.3) };
        var snap2b = new List<ResultItemViewModel> { ResultItem.Make("S2v2", 0.7) };
        var s2 = new MultiSnapshotSource(isInstant: false, [snap2a, snap2b]);
        var global = new GlobalSearch([s1, s2]);

        IReadOnlyList<ResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            last = snap;

        // Final state: S1 (0.5) + S2v2 (0.7), sorted → S2v2, S1
        Assert.Equal(2, last.Count);
        Assert.Equal("S2v2", last[0].Title);
        Assert.Equal("S1", last[1].Title);
        // S2v1 must NOT appear (it was replaced)
        Assert.DoesNotContain(last, r => r.Title == "S2v1");
    }

    // ── Limit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Limit_EnforcedOnMergedResults() {
        var s1 = new StubSource(isInstant: false, [
            ResultItem.Make("A", 1.0),
            ResultItem.Make("B", 0.9),
            ResultItem.Make("C", 0.8),
        ]);
        var s2 = new StubSource(isInstant: false, [
            ResultItem.Make("D", 0.7),
            ResultItem.Make("E", 0.6),
        ]);
        var global = new GlobalSearch([s1, s2]);

        IReadOnlyList<ResultItemViewModel> last = [];
        await foreach (var snap in global.SearchDeferredAsync("q", limit: 3))
            last = snap;

        Assert.Equal(3, last.Count);
        Assert.Equal("A", last[0].Title);
        Assert.Equal("B", last[1].Title);
        Assert.Equal("C", last[2].Title);
    }

    // ── Empty query / no sources ──────────────────────────────────────────────

    [Fact]
    public async Task NoSources_ReturnsNoSnapshots() {
        var global = new GlobalSearch([]);

        var count = 0;
        await foreach (var _ in global.SearchInstantAsync("q", 10))
            count++;
        await foreach (var _ in global.SearchDeferredAsync("q", 10))
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SourceReturnsEmptySnapshot_EmitsOneEmptySnapshot() {
        // A source that yields an empty list still causes one snapshot to be emitted
        // (the merged result of all slots, which is also empty).
        var source = new StubSource(isInstant: false, []);
        var global = new GlobalSearch([source]);

        var snapshots = new List<IReadOnlyList<ResultItemViewModel>>();
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            snapshots.Add(snap);

        Assert.Single(snapshots);
        Assert.Empty(snapshots[0]);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_StopsIteration() {
        var source = new BlockingSource(isInstant: false);
        var global = new GlobalSearch([source]);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in global.SearchDeferredAsync("q", 10, cts.Token))
                count++;
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Cancellation_AlreadyCancelled_ReturnsImmediately() {
        var source = new StubSource(isInstant: false, [ResultItem.Make("X", 1.0)]);
        var global = new GlobalSearch([source]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in global.SearchDeferredAsync("q", 10, cts.Token))
                count++;
        });

        Assert.Equal(0, count);
    }

    // ── WhenReady ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenReady_CompletesWhenAllSourcesReady() {
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        var s1 = new DelayedReadySource(tcs1, []);
        var s2 = new DelayedReadySource(tcs2, []);
        var global = new GlobalSearch([s1, s2]);

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
        var global = new GlobalSearch([]);
        await global.WhenReady(); // must not hang
    }

    // ── SearchInstantAsync / SearchDeferredAsync with no matching sources ─────

    [Fact]
    public async Task SearchInstantAsync_NoInstantSources_ReturnsNoSnapshots() {
        var deferred = new StubSource(isInstant: false, [ResultItem.Make("D", 1.0)]);
        var global = new GlobalSearch([deferred]);

        var count = 0;
        await foreach (var _ in global.SearchInstantAsync("q", 10))
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SearchDeferredAsync_NoDeferredSources_ReturnsNoSnapshots() {
        var instant = new StubSource(isInstant: true, [ResultItem.Make("I", 1.0)]);
        var global = new GlobalSearch([instant]);

        var count = 0;
        await foreach (var _ in global.SearchDeferredAsync("q", 10))
            count++;

        Assert.Equal(0, count);
    }

    // ── Mixed instant/deferred sources in same GlobalSearch ───────────────────

    [Fact]
    public async Task MixedSources_InstantAndDeferredSearchReturnCorrectSubsets() {
        var instant = new StubSource(isInstant: true, [ResultItem.Make("Instant", 0.8)]);
        var deferred = new StubSource(isInstant: false, [ResultItem.Make("Deferred", 0.9)]);
        var global = new GlobalSearch([instant, deferred]);

        IReadOnlyList<ResultItemViewModel> instantResult = [];
        await foreach (var snap in global.SearchInstantAsync("q", 10))
            instantResult = snap;

        IReadOnlyList<ResultItemViewModel> deferredResult = [];
        await foreach (var snap in global.SearchDeferredAsync("q", 10))
            deferredResult = snap;

        Assert.Single(instantResult);
        Assert.Equal("Instant", instantResult[0].Title);

        Assert.Single(deferredResult);
        Assert.Equal("Deferred", deferredResult[0].Title);
    }
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Date;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Date;

public class DateSearchTests
{
    private DateSearch BuildSearch(out ClipboardService clipboard, Action<UserSettings>? configure = null)
    {
        clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var settings = TestSettings.LoadIsolated(new FakePlatformProvider([]));
        configure?.Invoke(settings);
        return new DateSearch(settings, clipboard, NullLogger<DateSearch>.Instance);
    }

    /// <summary>
    /// Search runs recognition in a background thread and fires ResultChanged when done.
    /// Tests use this helper to drive the source synchronously: kick off the search, wait on
    /// the event, then read the cached result via a second Search call.
    /// </summary>
    private static IReadOnlyList<BaseResultItemViewModel> SearchAndWait(DateSearch search, string query, int limit)
    {
        using var ev = new System.Threading.ManualResetEventSlim();
        void Handler() => ev.Set();
        search.ResultChanged += Handler;
        try {
            var first = search.Search(query, limit);
            if (first.Count > 0) return first;
            if (!ev.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException($"DateSearch did not complete recognition for query \"{query}\" within 15s");
            return search.Search(query, limit);
        } finally {
            search.ResultChanged -= Handler;
        }
    }

    // ── 1. Spanish date ────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithSpanishDate_ReturnsResult()
    {
        var search  = BuildSearch(out _, null);
        var results = SearchAndWait(search, "3 de mayo", 5);

        var item = Assert.Single(results);
        var vm   = Assert.IsType<DateSearchResultViewModel>(item);

        Assert.Equal("📅",   vm.Icon);
        Assert.Equal("Date", vm.Category);
        Assert.Equal(3,      vm.Cells.Count);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", vm.Cells[0]);
    }

    // ── 2. English date ────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithEnglishDate_ReturnsResult()
    {
        var search  = BuildSearch(out _, null);
        var results = SearchAndWait(search, "next Monday", 5);

        var item = Assert.Single(results);
        Assert.IsType<DateSearchResultViewModel>(item);
    }

    // ── 3. Date range ──────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithDateRange_ReturnsRangeResult()
    {
        var search  = BuildSearch(out _, null);
        var results = SearchAndWait(search, "del 1 al 5 de junio", 5);

        var item = Assert.Single(results);
        var vm   = Assert.IsType<DateSearchResultViewModel>(item);

        Assert.Equal("Date Range", vm.Category);
        Assert.Equal(4, vm.Cells.Count);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", vm.Cells[0]); // isoStart
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", vm.Cells[1]); // isoEnd
        Assert.StartsWith("From ", vm.Cells[2]);               // "From X to Y"
        Assert.Equal(4, vm.CellSubtitles.Count);
    }

    // ── 4. Disabled ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_WhenDisabled_ReturnsEmpty()
    {
        var search  = BuildSearch(out _, s => s.DateSearchEnabled = false);
        var results = search.Search("3 de mayo", 5);

        Assert.Empty(results);
    }

    // ── 5. No date in query ────────────────────────────────────────────────────

    [Fact]
    public void Search_WithNoDate_ReturnsEmpty()
    {
        var search  = BuildSearch(out _);
        var results = search.Search("safari", 5);

        Assert.Empty(results);
    }

    // ── 6. Subtitles count matches cells count ────────────────────────────────

    [Fact]
    public void Search_WithDate_SubtitleCountMatchesCellCount()
    {
        var search  = BuildSearch(out _);
        var results = SearchAndWait(search, "3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        Assert.Equal(vm.Cells.Count, vm.CellSubtitles.Count);
    }

    // ── 7. Enter action copies selected cell ──────────────────────────────────

    [Fact]
    public void OnActivate_CopiesSelectedCell()
    {
        var search = BuildSearch(out var clipboard, null);

        string copied = "";
        clipboard.Initialize(
            copy: text => copied = text,
            read: () => Task.FromResult<string?>(null));

        var results = SearchAndWait(search, "3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        var enterAction = vm.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.Enter);
        Assert.NotNull(enterAction);
        enterAction.Execute();

        Assert.Equal(vm.Cells[0], copied);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", copied);
    }

    // ── 8. MoveCellRight wraps from last to first ──────────────────────────────

    [Fact]
    public void MoveCellRight_CyclesFromLastToFirst()
    {
        var search  = BuildSearch(out _, null);
        var results = SearchAndWait(search, "3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        // Navigate to last cell
        for (var i = 0; i < vm.Cells.Count - 1; i++)
            vm.MoveCellRight();

        Assert.Equal(vm.Cells.Count - 1, vm.SelectedCell);

        // One more right → wraps to first
        vm.MoveCellRight();
        Assert.Equal(0, vm.SelectedCell);
    }

    // ── 9. MoveCellLeft wraps from first to last ───────────────────────────────

    [Fact]
    public void MoveCellLeft_CyclesFromFirstToLast()
    {
        var search  = BuildSearch(out _, null);
        var results = SearchAndWait(search, "3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        Assert.Equal(0, vm.SelectedCell);

        // One left from first → wraps to last
        vm.MoveCellLeft();
        Assert.Equal(vm.Cells.Count - 1, vm.SelectedCell);
    }
}

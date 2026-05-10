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
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        configure?.Invoke(settings);
        return new DateSearch(settings, clipboard, NullLogger<DateSearch>.Instance);
    }

    // ── 1. Spanish date ────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithSpanishDate_ReturnsResult()
    {
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = ["es-es"]);
        var results = search.Search("3 de mayo", 5);

        var item = Assert.Single(results);
        var vm   = Assert.IsType<DateSearchResultViewModel>(item);

        Assert.Equal("📅",   vm.Icon);
        Assert.Equal("Date", vm.Category);
        Assert.Equal(2,      vm.Cells.Count);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", vm.Cells[0]);
    }

    // ── 2. English date ────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithEnglishDate_ReturnsResult()
    {
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = ["en-us"]);
        var results = search.Search("next Monday", 5);

        var item = Assert.Single(results);
        Assert.IsType<DateSearchResultViewModel>(item);
    }

    // ── 3. Date range ──────────────────────────────────────────────────────────

    [Fact]
    public void Search_WithDateRange_ReturnsRangeResult()
    {
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = ["es-es"]);
        var results = search.Search("del 1 al 5 de junio", 5);

        var item = Assert.Single(results);
        var vm   = Assert.IsType<DateSearchResultViewModel>(item);

        Assert.Equal("Date Range", vm.Category);
        Assert.Equal(4, vm.Cells.Count);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}/\d{4}-\d{2}-\d{2}$", vm.Cells[2]);
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

    // ── 6. Empty language list ─────────────────────────────────────────────────

    [Fact]
    public void Search_WithEmptyLanguages_ReturnsEmpty()
    {
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = []);
        var results = search.Search("3 de mayo", 5);

        Assert.Empty(results);
    }

    // ── 7. OnActivate copies selected cell ────────────────────────────────────

    [Fact]
    public void OnActivate_CopiesSelectedCell()
    {
        var search = BuildSearch(out var clipboard, s => s.DateSearchLanguages = ["es-es"]);

        string copied = "";
        clipboard.Initialize(
            copy: text => copied = text,
            read: () => Task.FromResult<string?>(null));

        var results = search.Search("3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        Assert.NotNull(vm.OnActivate);
        vm.OnActivate();

        Assert.Equal(vm.Cells[0], copied);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", copied);
    }

    // ── 8. MoveCellRight wraps from last to first ──────────────────────────────

    [Fact]
    public void MoveCellRight_CyclesFromLastToFirst()
    {
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = ["es-es"]);
        var results = search.Search("3 de mayo", 5);
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
        var search  = BuildSearch(out _, s => s.DateSearchLanguages = ["es-es"]);
        var results = search.Search("3 de mayo", 5);
        var vm      = Assert.IsType<DateSearchResultViewModel>(Assert.Single(results));

        Assert.Equal(0, vm.SelectedCell);

        // One left from first → wraps to last
        vm.MoveCellLeft();
        Assert.Equal(vm.Cells.Count - 1, vm.SelectedCell);
    }
}

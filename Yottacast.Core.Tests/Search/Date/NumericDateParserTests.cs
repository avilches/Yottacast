using Xunit;
using Yottacast.Core.Search.Date;

namespace Yottacast.Core.Tests.Search.Date;

public class NumericDateParserTests
{
    // ── ISO ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DateNumericOrder.DayFirst)]
    [InlineData(DateNumericOrder.MonthFirst)]
    public void TryParse_IsoDate_ReturnsIsoFormatNotAmbiguous(DateNumericOrder pref)
    {
        var r = NumericDateParser.TryParse("2025-12-24", pref);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2025, 12, 24), r!.Value.Date);
        Assert.Equal(NumericDateFormat.Iso, r.Value.Format);
        Assert.False(r.Value.Ambiguous);
    }

    // ── Obvious D/M ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DateNumericOrder.DayFirst)]
    [InlineData(DateNumericOrder.MonthFirst)]
    public void TryParse_ObviousDayMonth_ReturnsDayMonthYear(DateNumericOrder pref)
    {
        var r = NumericDateParser.TryParse("24-12-2025", pref);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2025, 12, 24), r!.Value.Date);
        Assert.Equal(NumericDateFormat.DayMonthYear, r.Value.Format);
        Assert.False(r.Value.Ambiguous);
    }

    // ── Obvious M/D ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DateNumericOrder.DayFirst)]
    [InlineData(DateNumericOrder.MonthFirst)]
    public void TryParse_ObviousMonthDay_ReturnsMonthDayYear(DateNumericOrder pref)
    {
        var r = NumericDateParser.TryParse("12-24-2025", pref);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2025, 12, 24), r!.Value.Date);
        Assert.Equal(NumericDateFormat.MonthDayYear, r.Value.Format);
        Assert.False(r.Value.Ambiguous);
    }

    // ── Ambiguous resolution by preference ──────────────────────────────────────

    [Fact]
    public void TryParse_Ambiguous_DayFirst_PicksDayMonth()
    {
        var r = NumericDateParser.TryParse("04-03-2015", DateNumericOrder.DayFirst);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2015, 3, 4), r!.Value.Date); // day 4, month 3
        Assert.Equal(NumericDateFormat.DayMonthYear, r.Value.Format);
        Assert.True(r.Value.Ambiguous);
    }

    [Fact]
    public void TryParse_Ambiguous_MonthFirst_PicksMonthDay()
    {
        var r = NumericDateParser.TryParse("04-03-2015", DateNumericOrder.MonthFirst);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2015, 4, 3), r!.Value.Date); // month 4, day 3
        Assert.Equal(NumericDateFormat.MonthDayYear, r.Value.Format);
        Assert.True(r.Value.Ambiguous);
    }

    // ── Separators ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("24/12/2025")]
    [InlineData("24.12.2025")]
    public void TryParse_AcceptsSlashAndDotSeparators(string input)
    {
        var r = NumericDateParser.TryParse(input, DateNumericOrder.DayFirst);

        Assert.NotNull(r);
        Assert.Equal(new DateTime(2025, 12, 24), r!.Value.Date);
        Assert.Equal(NumericDateFormat.DayMonthYear, r.Value.Format);
    }

    // ── Invalid → null ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("31-02-2025")]   // impossible calendar day
    [InlineData("2025-2024-01")] // two years
    [InlineData("12-5-25")]      // no 4-digit year
    [InlineData("16/9")]         // fewer than three components
    [InlineData("1/2")]
    [InlineData("12.5")]
    [InlineData("134.2")]
    [InlineData("04-03/2025")]   // mixed separators
    [InlineData("13-13-2025")]   // both > 12
    public void TryParse_InvalidInputs_ReturnNull(string input)
    {
        Assert.Null(NumericDateParser.TryParse(input, DateNumericOrder.DayFirst));
        Assert.Null(NumericDateParser.TryParse(input, DateNumericOrder.MonthFirst));
    }

    // ── FormatLabel ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NumericDateFormat.Iso, "YYYY-MM-DD")]
    [InlineData(NumericDateFormat.DayMonthYear, "DD/MM/YYYY")]
    [InlineData(NumericDateFormat.MonthDayYear, "MM/DD/YYYY")]
    public void FormatLabel_ReturnsExpectedLabel(NumericDateFormat format, string expected)
    {
        Assert.Equal(expected, NumericDateParser.FormatLabel(format));
    }
}

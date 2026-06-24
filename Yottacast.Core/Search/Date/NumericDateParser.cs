using System.Text.RegularExpressions;

namespace Yottacast.Core.Search.Date;

/// <summary>Resolution order applied to ambiguous numeric dates (both components ≤ 12, year last).</summary>
public enum DateNumericOrder { DayFirst, MonthFirst }

/// <summary>Which field order a numeric date string was interpreted with.</summary>
public enum NumericDateFormat { Iso, DayMonthYear, MonthDayYear }

/// <summary>
/// Parses purely numeric date strings ("2025-12-24", "24-12-2025", "12/24/2025", "24.12.2025")
/// without involving the locale-driven natural-language recognizer. Requires three components
/// separated by a single, consistent separator (- / .) and a 4-digit year in the first or last
/// position. Ambiguous day/month order (both ≤ 12, year last) is resolved by the given preference.
/// </summary>
public static class NumericDateParser
{
    public readonly record struct Result(DateTime Date, NumericDateFormat Format, bool Ambiguous);

    private static readonly Regex Pattern =
        new(@"^\s*(\d{1,4})([-/.])(\d{1,2})\2(\d{1,4})\s*$", RegexOptions.Compiled);

    /// <summary>Returns the parsed date, or null when the text is not a valid numeric date.</summary>
    public static Result? TryParse(string text, DateNumericOrder preference)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Pattern.Match(text);
        if (!m.Success) return null;

        var aLen = m.Groups[1].Value.Length;
        var cLen = m.Groups[4].Value.Length;
        var a = int.Parse(m.Groups[1].Value);
        var b = int.Parse(m.Groups[3].Value);
        var c = int.Parse(m.Groups[4].Value);

        var aIsYear = aLen == 4;
        var cIsYear = cLen == 4;
        if (aIsYear == cIsYear) return null; // need exactly one 4-digit year (rejects zero or two)

        int year, month, day;
        NumericDateFormat format;
        var ambiguous = false;

        if (aIsYear) {
            year = a; month = b; day = c;            // ISO: year-month-day
            format = NumericDateFormat.Iso;
        } else {
            year = c;                                 // year last; a and b are day/month
            if (a > 12 && b <= 12) { day = a; month = b; format = NumericDateFormat.DayMonthYear; }
            else if (b > 12 && a <= 12) { month = a; day = b; format = NumericDateFormat.MonthDayYear; }
            else if (a <= 12 && b <= 12) {
                ambiguous = true;
                if (preference == DateNumericOrder.DayFirst) { day = a; month = b; format = NumericDateFormat.DayMonthYear; }
                else { month = a; day = b; format = NumericDateFormat.MonthDayYear; }
            } else return null;                       // both > 12
        }

        if (year is < 1 or > 9999 || month is < 1 or > 12 || day is < 1 or > 31) return null;
        try { return new Result(new DateTime(year, month, day), format, ambiguous); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Human-readable interpretation label shown as the result subtitle.</summary>
    public static string FormatLabel(NumericDateFormat format) => format switch {
        NumericDateFormat.DayMonthYear => "DD/MM/YYYY",
        NumericDateFormat.MonthDayYear => "MM/DD/YYYY",
        _                              => "YYYY-MM-DD",
    };
}

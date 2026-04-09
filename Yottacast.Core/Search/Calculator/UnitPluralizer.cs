using System.Globalization;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Shared pluralization helper for unit long names.
/// Handles simple units ("meter" → "meters"), irregular plurals ("foot" → "feet", "inch" → "inches"),
/// invariant suffixes ("fahrenheit", "hertz"/"kilohertz"/…), and compound "X per Y" names where
/// only the first word is pluralized ("kilometer per hour" → "kilometers per hour").
/// </summary>
internal static class UnitPluralizer {
    public static string Pluralize(string name, string valueStr) {
        if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return name;
        if (Math.Abs(d) == 1.0) return name;
        return PluralizeForMultiple(name);
    }

    private static string PluralizeForMultiple(string name) {
        if (name == "foot") return "feet";
        if (name == "inch") return "inches";
        if (name.EndsWith("hertz")) return name;   // hertz, kilohertz, megahertz … invariantes

        // "X per Y": pluralizar solo la primera palabra
        var perIdx = name.IndexOf(" per ", StringComparison.Ordinal);
        if (perIdx > 0) {
            var first = name[..perIdx];
            return PluralizeForMultiple(first) + name[perIdx..];
        }

        if (name.EndsWith('s') || name.EndsWith("heit")) return name;
        return name + "s";
    }
}

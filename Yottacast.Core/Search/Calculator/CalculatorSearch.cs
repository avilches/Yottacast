using System.Globalization;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Instant search source that evaluates math expressions and unit conversions via math.js (Jint).
/// Activating the result copies the value to the clipboard.
/// Validation is fully delegated to math.js — if Evaluate returns null, there is no result.
/// When the expression fails with an actionable error (unknown unit, incompatible units) an
/// informational error item is shown via LastHint.
/// </summary>
public class CalculatorSearch(MathJsEngine engine, ClipboardService clipboard) : IInstantSearchSource, ISearchHintProvider {
    public string? LastHint { get; private set; }

    public void Start() { }
    public Task WhenReady() => engine.WhenReady();
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        var q = query.Trim();

        switch (engine.Evaluate(q)) {
            case ConversionResult r: {
                var fromUnit      = engine.DisplayUnit(r.FromUnit);
                var toUnit        = engine.DisplayUnit(r.ToUnit);
                var fromShort     = $"{r.FromValue} {fromUnit}".Trim();
                var toShort       = $"{r.ToValue} {toUnit}".Trim();
                var fromLong      = LongForm(r.FromValue, r.FromUnitLong, r.FromUnit);
                var toLong        = string.IsNullOrEmpty(r.ToUnit)
                    ? (r.ToUnitLong is not null && r.ToUnitLong != toShort ? r.ToUnitLong : null)
                    : LongForm(r.ToValue, r.ToUnitLong, r.ToUnit);
                var ambiguityHint = BuildHints(r.AmbiguityHints);
                var captured      = toShort;
                return [new ConversionResultItemViewModel {
                    Icon          = "📐",
                    Category      = "Converter",
                    Score         = 4,
                    FromShort     = fromShort,
                    FromLong      = fromLong,
                    ToShort       = toShort,
                    ToLong        = toLong,
                    AmbiguityHint = string.IsNullOrEmpty(ambiguityHint) ? null : ambiguityHint,
                    OnActivate    = () => clipboard.CopyText(captured),
                }];
            }
            case CalcResult r when r.RawValue != q: {
                var subtitle = BuildSubtitle(r.NormalizedQuery, r.AmbiguityHints);
                var captured = r.RawValue;
                return [new ResultItemViewModel {
                    Icon = "🧮",
                    Title = r.RawValue,
                    Subtitle = subtitle,
                    Category = "Calculator",
                    Score = 4,
                    OnActivate = () => clipboard.CopyText(captured),
                }];
            }
            case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnits:
                LastHint = BuildErrorHint(r);
                break;
        }

        return [];
    }

    private static string BuildSubtitle(string normalizedQuery, IReadOnlyList<AmbiguityHint>? hints) {
        var hintText = BuildHints(hints);
        return string.IsNullOrEmpty(hintText) ? normalizedQuery : $"{normalizedQuery}   {hintText}";
    }

    private static string BuildHints(IReadOnlyList<AmbiguityHint>? hints) {
        if (hints is not { Count: > 0 }) return "";
        var parts = hints.Select(h => {
            // Candidates[0] is the one selected; show the alternatives
            var alternatives = h.Candidates.Skip(1)
                .GroupBy(c => c.LongName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (alternatives.Count == 0) return null;
            var altText = string.Join(" or ", alternatives.Select(c => $"{c.Symbol} ({c.LongName})"));
            return $"Maybe you meant {altText}?";
        }).OfType<string>();
        return string.Join("  ", parts);
    }

    private static string Pluralize(string name, string valueStr) {
        if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return name;
        if (Math.Abs(d) == 1.0) return name;
        if (name == "foot")  return "feet";
        if (name == "inch")  return "inches";
        if (name == "hertz") return "hertz";
        // "X per Y" compound names: pluralize first word only (e.g. "kilometer per hour" → "kilometers per hour")
        var perIdx = name.IndexOf(" per ", StringComparison.Ordinal);
        if (perIdx > 0) {
            var first = name[..perIdx];
            var pluralFirst = first switch {
                "foot" => "feet",
                "inch" => "inches",
                _      => first.EndsWith('s') ? first : first + "s"
            };
            return pluralFirst + name[perIdx..];
        }
        return name.EndsWith('s') || name.EndsWith("heit") ? name : name + "s";
    }

    private static string? LongForm(string value, string? unitLong, string unitShort) {
        if (unitLong == null) return null;
        var s = $"{value} {Pluralize(unitLong, value)}";
        var shortForm = $"{value} {unitShort}".Trim();
        return s == shortForm ? null : s;
    }

    private static string BuildErrorHint(ErrorResult r) => r.ErrorKind switch {
        CalcErrorKind.UnknownSymbol =>
            $"Unknown unit or variable: '{r.ErrorToken}'",
        CalcErrorKind.IncompatibleUnits =>
            r.ErrorMessage ?? "Incompatible units",
        _ => r.ErrorMessage ?? "Error"
    };
}

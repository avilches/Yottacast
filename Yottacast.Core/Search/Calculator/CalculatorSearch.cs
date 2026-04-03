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

    public IReadOnlyList<ResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        var q = query.Trim();

        switch (engine.Evaluate(q)) {
            case ConversionResult r: {
                var fromUnit  = engine.DisplayUnit(r.FromUnit);
                var toUnit    = engine.DisplayUnit(r.ToUnit);
                var fromShort = $"{r.FromValue} {fromUnit}".Trim();
                var toShort   = $"{r.ToValue} {toUnit}".Trim();
                var fromLong  = LongForm(r.FromValue, r.FromUnitLong, r.FromUnit);
                var toLong    = LongForm(r.ToValue,   r.ToUnitLong,   r.ToUnit);
                var captured  = toShort;
                return [new ConversionResultItemViewModel {
                    Icon      = "📐",
                    Title     = toShort,
                    Subtitle  = BuildSubtitle(r.NormalizedQuery, r.AmbiguityHints),
                    Category  = "Converter",
                    Score     = 4,
                    FromShort = fromShort,
                    FromLong  = fromLong,
                    ToShort   = toShort,
                    ToLong    = toLong,
                    OnActivate = () => clipboard.CopyText(captured),
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
            case ErrorResult r when r.ErrorKind is CalcErrorKind.UnknownSymbol or CalcErrorKind.IncompatibleUnits:
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
            var candidates = string.Join(" · ", h.Candidates.Select(c => $"{c.Symbol}={c.LongName}"));
            return $"'{h.Input}', {candidates}";
        });
        return $"⚠ {string.Join("; ", parts)}";
    }

    private static string Pluralize(string name, string valueStr) {
        if (!double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return name;
        if (Math.Abs(d) == 1.0) return name;
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

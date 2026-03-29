using System.Text.RegularExpressions;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Instant search source that evaluates math expressions and unit conversions via math.js (Jint).
/// Activating the result copies the value to the clipboard.
/// Validation is fully delegated to math.js — if Evaluate returns null, there is no result.
/// When the expression fails with an actionable error (unknown unit, wrong casing, incompatible
/// units) and the query looks math-like, an informational error item is shown instead.
/// </summary>
public class CalculatorSearch(MathJsEngine engine, ClipboardService clipboard) : IInstantSearchSource, ISearchHintProvider {
    public string? LastHint { get; private set; }

    public void Start() { }
    public Task WhenReady() => engine.WhenReady();
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<ResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        var q = query.Trim();
        var result = engine.Evaluate(q);

        if (result.IsSuccess && result.Value != q) {
            var capturedResult = result.Value!;
            var subtitle = result.NormalizedQuery;
            if (result.AmbiguityHints is { Count: > 0 } hints) {
                var hintParts = hints.Select(h => {
                    var candidates = string.Join(" · ", h.Candidates.Select(c => $"{c.Symbol}={c.LongName}"));
                    return $"'{h.Input}', {candidates}";
                });
                subtitle = $"{q}   ⚠ {string.Join("; ", hintParts)}";
            }
            return [new ResultItemViewModel {
                Icon = result.IsConversion ? "📐" : "🧮",
                Title = capturedResult,
                Subtitle = subtitle,
                Category = result.IsConversion ? "Converter" : "Calculator",
                Score = 4,
                OnActivate = () => clipboard.CopyText(capturedResult),
            }];
        }

        if (result.ErrorKind is
                CalcErrorKind.WrongUnitCasing or
                CalcErrorKind.UnknownSymbol or
                CalcErrorKind.IncompatibleUnits) {
            LastHint = BuildErrorTitle(result);
        }

        return [];
    }

    private static string BuildErrorTitle(EvaluationResult result) => result.ErrorKind switch {
        CalcErrorKind.WrongUnitCasing when result.ErrorSuggestions is { Count: > 0 } suggestions =>
            $"'{result.ErrorToken}' not found – did you mean: {string.Join(" · ", suggestions.Select(s => $"{s.Symbol} ({s.LongName})"))}?",
        CalcErrorKind.UnknownSymbol =>
            $"Unknown unit or variable: '{result.ErrorToken}'",
        CalcErrorKind.IncompatibleUnits =>
            result.Error ?? "Incompatible units",
        _ => result.Error ?? "Error"
    };

    /// Returns true when the query looks like a math expression (digits or operators),
    /// so we avoid showing error items for random non-math text like "safari".
    private static bool LooksMathLike(string q) =>
        q.Any(char.IsDigit) ||
        q.IndexOfAny(['+', '*', '/', '^', '%']) >= 0 ||
        Regex.IsMatch(q, @"\bto\b", RegexOptions.IgnoreCase);
}

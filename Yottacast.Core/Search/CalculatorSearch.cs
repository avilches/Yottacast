using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search;

/// <summary>
/// Instant search source that evaluates math expressions and unit conversions via math.js (Jint).
/// Activating the result copies the value to the clipboard.
/// Validation is fully delegated to math.js — if Evaluate returns null, there is no result.
/// </summary>
public class CalculatorSearch(MathJsEngine engine, ClipboardService clipboard) : ISearchSource {

    // NUMBER UNIT (to|in|en) UNIT — shown as converter instead of calculator
    private static readonly Regex UnitConvPattern = new(
        @"^\d+(?:[.,]\d+)?\s+[a-zA-Zμ°/²³]+\s+(?:to|in|en)\s+[a-zA-Zμ°/²³]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool IsInstant => true;
    public void Start() { }
    public Task WhenReady() => engine.WhenReady();
    public Task Stop() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<ResultItemViewModel>> SearchAsync(
        string query, int limit, [EnumeratorCancellation] CancellationToken ct = default) {

        var q = query.Trim();
        var result = engine.Evaluate(q);

        if (result == null || result == q)
            yield break;

        var isConversion = UnitConvPattern.IsMatch(q);
        var capturedResult = result;
        var capturedQuery = q;

        yield return [new ResultItemViewModel {
            Icon = isConversion ? "📐" : "🧮",
            Title = capturedResult,
            Subtitle = capturedQuery,
            Category = isConversion ? "Converter" : "Calculator",
            Score = 4,
            OnActivate = () => clipboard.CopyText(capturedResult),
        }];

        await Task.CompletedTask;
    }
}

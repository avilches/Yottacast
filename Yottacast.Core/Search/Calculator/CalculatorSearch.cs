using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Instant search source that evaluates math expressions and unit conversions via math.js (Jint).
/// Activating the result copies the value to the clipboard.
/// Validation is fully delegated to math.js — if Evaluate returns null, there is no result.
/// </summary>
public class CalculatorSearch(MathJsEngine engine, ClipboardService clipboard) : IInstantSearchSource {

    public void Start() { }
    public Task WhenReady() => engine.WhenReady();
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<ResultItemViewModel> Search(string query, int _) {
        var q = query.Trim();
        var result = engine.Evaluate(q);

        if (!result.IsSuccess || result.Value == q)
            return [];

        var capturedResult = result.Value;

        return [new ResultItemViewModel {
            Icon = result.IsConversion ? "📐" : "🧮",
            Title = capturedResult,
            Subtitle = q,
            Category = result.IsConversion ? "Converter" : "Calculator",
            Score = 4,
            OnActivate = () => clipboard.CopyText(capturedResult),
        }];
    }
}

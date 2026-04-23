using Microsoft.Extensions.Logging;
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
public class CalculatorSearch(MathJsEngine engine, ClipboardService clipboard, UserSettings settings, ILogger<CalculatorSearch> logger) : IInstantSearchSource, ISearchHintProvider {
    public string? LastHint { get; private set; }

    public void Start() { }
    public Task WhenReady() => engine.WhenReady();
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        if (!settings.EnableCalculator) return [];
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

                string? normFromShort = null, normFromLong = null;
                if (r.NormFromUnit != null && r.NormFromValue != null) {
                    var normFromUnit = engine.DisplayUnit(r.NormFromUnit);
                    normFromShort = $"{r.NormFromValue} {normFromUnit}".Trim();
                    normFromLong  = LongForm(r.NormFromValue, r.NormFromUnitLong, r.NormFromUnit);
                }

                logger.LogDebug("Calculator query=\"{Query}\" → conversion {From} → {To}", q, fromShort, toShort);

                var capturedOrig = fromShort;
                var capturedNorm = normFromShort;
                var capturedTo   = toShort;
                ConversionResultItemViewModel vm = null!;
                vm = new ConversionResultItemViewModel {
                    Icon              = "📐",
                    Category          = "Converter",
                    Score             = 4,
                    FromShort         = fromShort,
                    FromLong          = fromLong,
                    NormFromShort     = normFromShort,
                    NormFromLong      = normFromLong,
                    ToShort           = toShort,
                    ToLong            = toLong,
                    AmbiguityHint     = string.IsNullOrEmpty(ambiguityHint) ? null : ambiguityHint,
                    FromWasNormalized = r.FromWasNormalized,
                    OnLeft  = () => vm.MoveCellLeft(),
                    OnRight = () => vm.MoveCellRight(),
                    OnActivate = () => {
                        var copied = vm.SelectedCell switch {
                            ConversionCell.OrigFrom => capturedOrig,
                            ConversionCell.NormFrom => capturedNorm ?? capturedTo,
                            _                       => capturedTo,
                        };
                        logger.LogInformation("Calculator: copied conversion result \"{Value}\"", copied);
                        clipboard.CopyText(copied);
                    },
                };
                return [vm];
            }
            case CalcResult r when r.RawValue != q: {
                logger.LogDebug("Calculator query=\"{Query}\" → result \"{Result}\"", q, r.RawValue);
                var subtitle = BuildSubtitle(r.NormalizedQuery, r.AmbiguityHints);
                var captured = r.RawValue;
                return [new CalculatorResultItemViewModel {
                    Icon = "🧮",
                    Title = r.RawValue,
                    Subtitle = subtitle,
                    Category = "Calculator",
                    Score = 4,
                    OnActivate = () => {
                        logger.LogInformation("Calculator: copied result \"{Value}\"", captured);
                        clipboard.CopyText(captured);
                    },
                }];
            }
            case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnitsConvert or CalcErrorKind.IncompatibleUnitsOp:
                LastHint = BuildErrorHint(r);
                logger.LogDebug("Calculator query=\"{Query}\" → error {Kind}: {Hint}", q, r.ErrorKind, LastHint);
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

    private static string? LongForm(string value, string? unitLong, string unitShort) {
        if (unitLong == null) return null;
        var s = $"{value} {UnitPluralizer.Pluralize(unitLong, value)}";
        var shortForm = $"{value} {unitShort}".Trim();
        return s == shortForm ? null : s;
    }

    private static string BuildErrorHint(ErrorResult r) => r.ErrorKind switch {
        CalcErrorKind.UnknownSymbol =>
            $"Unknown unit or variable: '{r.ErrorToken}'",
        CalcErrorKind.IncompatibleUnitsConvert => BuildCantConvertMessage(r.ErrorToken),
        CalcErrorKind.IncompatibleUnitsOp =>
            "Units do not match",
        _ => r.ErrorMessage ?? "Error"
    };

    private static string BuildCantConvertMessage(string? token) {
        if (token == null) return "Can't convert between these units";
        var parts = token.Split('|');
        return parts.Length == 2 ? $"Can't convert {parts[0]} to {parts[1]}" : "Can't convert between these units";
    }
}

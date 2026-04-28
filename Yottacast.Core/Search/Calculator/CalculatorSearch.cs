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
public class CalculatorSearch(MathJsEngineProvider engineProvider, ExchangeRateService exchangeRateService, ClipboardService clipboard, UserSettings settings, ILogger<CalculatorSearch> logger) : IInstantSearchSource, ISearchHintProvider {
    public string? LastHint { get; private set; }

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        if (!settings.EnableCalculator) return [];
        var engine = engineProvider.Current;
        if (engine == null) return [];
        var q = query.Trim();

        // "EUR" → "1 EUR": bare currency code without a value should trigger the default-pair conversion.
        // Non-currency tokens (e.g. "km") are not registered as currencies in the engine so they are unaffected.
        if (q.Length is >= 2 and <= 10 && q.All(char.IsLetter)) {
            var upper = q.ToUpperInvariant();
            if (engine.IsKnownCurrency(upper))
                q = "1 " + upper;
        }

        switch (engine.Evaluate(q)) {
            case ConversionResult r: {
                var fromUnit      = engine.DisplayUnit(r.FromUnit);
                var toUnit        = engine.DisplayUnit(r.ToUnit);
                var fromShort     = $"{r.FromValue} {fromUnit}".Trim();
                var toShort       = $"{r.ToValue} {toUnit}".Trim();
                var fromLong      = engine.IsKnownCurrency(r.FromUnit)
                    ? CurrencyClassifier.GetDisplayName(r.FromUnit)
                    : LongForm(r.FromValue, r.FromUnitLong, r.FromUnit);
                var toLong        = !string.IsNullOrEmpty(r.ToUnit) && engine.IsKnownCurrency(r.ToUnit)
                    ? CurrencyClassifier.GetDisplayName(r.ToUnit)
                    : string.IsNullOrEmpty(r.ToUnit)
                        ? (r.ToUnitLong is not null && r.ToUnitLong != toShort ? r.ToUnitLong : null)
                        : LongForm(r.ToValue, r.ToUnitLong, r.ToUnit);
                LastHint = BuildHints(r.AmbiguityHints) is { Length: > 0 } h ? h : null;

                string? normFromShort = null, normFromLong = null;
                if (r.NormFromUnit != null && r.NormFromValue != null) {
                    var normFromUnit = engine.DisplayUnit(r.NormFromUnit);
                    normFromShort = $"{r.NormFromValue} {normFromUnit}".Trim();
                    normFromLong  = engine.IsKnownCurrency(r.NormFromUnit)
                        ? CurrencyClassifier.GetDisplayName(r.NormFromUnit)
                        : LongForm(r.NormFromValue, r.NormFromUnitLong, r.NormFromUnit);
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
                    FromWasNormalized = r.FromWasNormalized,
                    RatesAreStale     = exchangeRateService.IsStale,
                    OnLeft  = r.FromWasNormalized ? () => vm.MoveCellLeft() : null,
                    OnRight = r.FromWasNormalized ? () => vm.MoveCellRight() : null,
                    OnActivate = () => {
                        var copied = vm.SelectedCell switch {
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
                LastHint = BuildHints(r.AmbiguityHints) is { Length: > 0 } ch ? ch : null;
                var subtitle = r.NormalizedQuery;
                var captured = r.RawValue;
                var titleLong = r.Unit != null
                    ? LongForm(r.RawValue.Replace($" {r.Unit}", "").Trim(), r.UnitLong, r.Unit)
                    : null;
                return [new CalculatorResultItemViewModel {
                    Icon = "🧮",
                    Title = r.RawValue,
                    TitleLong = titleLong,
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

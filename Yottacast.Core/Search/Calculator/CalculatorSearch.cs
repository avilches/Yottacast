using System.Globalization;
using Microsoft.Extensions.Logging;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Instant search source that evaluates math expressions and unit conversions via math.js (Jint).
/// Activating the result copies the value to the clipboard.
/// Validation is fully delegated to math.js — if Evaluate returns null, there is no result.
/// When the expression fails with an actionable error (unknown unit, incompatible units) an
/// informational error item is shown via LastHint. LastHintKind classifies the hint: Error for
/// incompatible-unit failures, Info for ambiguity suggestions and all other cases.
/// </summary>
public class CalculatorSearch(MathJsEngineProvider engineProvider, ExchangeRateService exchangeRateService, ClipboardService clipboard, UserSettings settings, ILogger<CalculatorSearch> logger, NerdamerEngine nerdamerEngine) : IInstantSearchSource, ISearchHintProvider {
    public string? LastHint { get; private set; }
    public SearchHintKind LastHintKind { get; private set; }
    public int Limit => AppDefaults.CalcSearchLimit;

    public void Start() { }
    public Task WhenReady() => Task.CompletedTask;
    public Task Stop() => Task.CompletedTask;

    public IReadOnlyList<BaseResultItemViewModel> Search(string query, int _) {
        LastHint = null;
        LastHintKind = SearchHintKind.Info;
        if (!settings.EnableCalculator) return [];
        var q = query.Trim();

        // Equation detection: queries containing '=' are routed to NerdamerEngine.
        // math.js already rejects assignments, so these queries would return empty anyway.
        if (q.Contains('=')) {
            var solveResult = nerdamerEngine.TrySolve(q, settings.CalculatorDecimalPlaces);
            if (solveResult != null) return BuildEquationResult(solveResult, q);
            return [];
        }

        var engine = engineProvider.Current;
        if (engine == null) return [];

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
                LastHintKind = SearchHintKind.Info;

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
                var isCurrencyConversion = engine.IsKnownCurrency(r.FromUnit)
                    || (!string.IsNullOrEmpty(r.ToUnit) && engine.IsKnownCurrency(r.ToUnit));
                ConversionResultItemViewModel vm = null!;
                vm = new ConversionResultItemViewModel {
                    Icon              = "📐",
                    Category          = "Converter",
                    Score             = 7,
                    ScoreReason       = "Conversión detectada",
                    FromShort         = fromShort,
                    FromLong          = fromLong,
                    NormFromShort     = normFromShort,
                    NormFromLong      = normFromLong,
                    ToShort           = toShort,
                    ToLong            = toLong,
                    FromWasNormalized = r.FromWasNormalized,
                    RatesAreStale     = isCurrencyConversion && exchangeRateService.IsStale,
                    RatesDateText     = isCurrencyConversion
                        ? exchangeRateService.RatesDate?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                        : null,
                    OnLeft  = r.FromWasNormalized ? () => vm.MoveCellLeft() : null,
                    OnRight = r.FromWasNormalized ? () => vm.MoveCellRight() : null,
                    Actions = [
                        new() {
                            Label           = "Copy value",
                            Hotkey          = ActionHotkey.Enter,
                            ShowInFooter    = true,
                            ShowInMenu      = true,
                            ClosesMenu      = true,
                            ClosesWindow    = true,
                            PasteAfterClose = true,
                            Execute = () => {
                                var copied = vm.SelectedCell switch {
                                    ConversionCell.NormFrom => capturedNorm ?? capturedTo,
                                    _                       => capturedTo,
                                };
                                logger.LogInformation("Calculator: copied conversion result \"{Value}\"", copied);
                                clipboard.CopyText(copied);
                            },
                        },
                        new() {
                            Label        = "Copy value",
                            Hotkey       = ActionHotkey.MetaC,
                            ShowInFooter = true,
                            ShowInMenu   = true,
                            ClosesMenu   = true,
                            HintProvider = () => "Result copied!",
                            Execute = () => {
                                var copied = vm.SelectedCell switch {
                                    ConversionCell.NormFrom => capturedNorm ?? capturedTo,
                                    _                       => capturedTo,
                                };
                                logger.LogInformation("Calculator: copied conversion via Cmd+C \"{Value}\"", copied);
                                clipboard.CopyText(copied);
                            },
                        },
                    ],
                };
                return [vm];
            }
            case CalcResult r when r.RawValue != q: {
                if (!settings.EnableCalculator) return [];
                logger.LogDebug("Calculator query=\"{Query}\" → result \"{Result}\"", q, r.RawValue);
                LastHint = BuildHints(r.AmbiguityHints) is { Length: > 0 } ch ? ch : null;
                LastHintKind = SearchHintKind.Info;
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
                    Score = 7,
                    ScoreReason = "Expresión detectada",
                    Actions = [
                        new() {
                            Label           = "Copy result",
                            Hotkey          = ActionHotkey.Enter,
                            ShowInFooter    = true,
                            ShowInMenu      = true,
                            ClosesMenu      = true,
                            ClosesWindow    = true,
                            PasteAfterClose = true,
                            Execute = () => {
                                logger.LogInformation("Calculator: copied result \"{Value}\"", captured);
                                clipboard.CopyText(captured);
                            },
                        },
                        new() {
                            Label        = "Copy result",
                            Hotkey       = ActionHotkey.MetaC,
                            ShowInFooter = true,
                            ShowInMenu   = true,
                            ClosesMenu   = true,
                            HintProvider = () => "Result copied!",
                            Execute = () => {
                                logger.LogInformation("Calculator: copied result via Cmd+C \"{Value}\"", captured);
                                clipboard.CopyText(captured);
                            },
                        },
                    ],
                }];
            }
            case ErrorResult r when r.ErrorKind is CalcErrorKind.IncompatibleUnitsConvert or CalcErrorKind.IncompatibleUnitsOp:
                LastHint = BuildErrorHint(r);
                LastHintKind = SearchHintKind.Error;
                logger.LogDebug("Calculator query=\"{Query}\" → error {Kind}: {Hint}", q, r.ErrorKind, LastHint);
                break;
            case ErrorResult r when r.ErrorKind == CalcErrorKind.UnknownSymbol: {
                if (q.Length < AppDefaults.AlgebraMinQueryLength) break;
                var algebraResult = nerdamerEngine.TryAlgebra(q, settings.CalculatorDecimalPlaces);
                if (algebraResult != null) return BuildAlgebraResult(algebraResult, q);
                break;
            }
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

    private IReadOnlyList<BaseResultItemViewModel> BuildAlgebraResult(AlgebraResult result, string originalQuery) {
        logger.LogDebug("Algebra query=\"{Query}\" → {Count} cells: {Labels}",
            originalQuery, result.Cells.Length,
            string.Join(", ", result.Cells.Select(c => c.Label)));

        AlgebraResultItemViewModel vm = null!;
        vm = new AlgebraResultItemViewModel {
            Title       = result.Cells[0].Result,
            Cells       = result.Cells,
            Score       = AppDefaults.AlgebraResultScore,
            ScoreReason = "Álgebra simbólica",
            OnLeft      = result.Cells.Length > 1 ? () => vm.MoveCellLeft()  : null,
            OnRight     = result.Cells.Length > 1 ? () => vm.MoveCellRight() : null,
            Actions = [
                new() {
                    Label           = "Copy result",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute = () => {
                        var copied = vm.CellItems[vm.SelectedCell].Result;
                        logger.LogInformation("Algebra: copied \"{Value}\"", copied);
                        clipboard.CopyText(copied);
                    },
                },
                new() {
                    Label        = "Copy result",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => "Result copied!",
                    Execute = () => {
                        var copied = vm.CellItems[vm.SelectedCell].Result;
                        logger.LogInformation("Algebra: copied via Cmd+C \"{Value}\"", copied);
                        clipboard.CopyText(copied);
                    },
                },
            ],
        };
        return [vm];
    }

    private IReadOnlyList<BaseResultItemViewModel> BuildEquationResult(SolveResult result, string originalQuery) {
        var first = result.Variables[0];
        var solutionsText = string.Join(", ", first.Solutions);
        var title = $"{first.Variable} = {solutionsText}";
        var captured = solutionsText;

        logger.LogDebug("Equation query=\"{Query}\" → {Title}", originalQuery, title);

        return [new CalculatorResultItemViewModel {
            Icon = "🧮",
            Title = title,
            Subtitle = originalQuery,
            Category = "Calculator",
            Score = 7,
            ScoreReason = "Expresión detectada",
            Actions = [
                new() {
                    Label           = "Copy result",
                    Hotkey          = ActionHotkey.Enter,
                    ShowInFooter    = true,
                    ShowInMenu      = true,
                    ClosesMenu      = true,
                    ClosesWindow    = true,
                    PasteAfterClose = true,
                    Execute = () => {
                        logger.LogInformation("Equation: copied result \"{Value}\"", captured);
                        clipboard.CopyText(captured);
                    },
                },
                new() {
                    Label        = "Copy result",
                    Hotkey       = ActionHotkey.MetaC,
                    ShowInFooter = true,
                    ShowInMenu   = true,
                    ClosesMenu   = true,
                    HintProvider = () => "Result copied!",
                    Execute = () => {
                        logger.LogInformation("Equation: copied result via Cmd+C \"{Value}\"", captured);
                        clipboard.CopyText(captured);
                    },
                },
            ],
        }];
    }
}

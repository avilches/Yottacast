using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jint;

namespace Yottacast.Core.Search.Calculator;

public enum ExprKind { Calculation, UnitEntry, SimpleConversion, ComplexConversion }

public record NormalizedExpression(
    string Expr, ExprKind Kind,
    string? FromUnit, string? ToUnit, string? LeftExpr,
    HashSet<string> Currencies, List<AmbiguityHint> Ambiguities);

/// <summary>
/// Controls decimal formatting in <see cref="MathJsEngine"/>.
/// </summary>
/// <param name="LargeNumberDecimals">Decimal places for results where |n| ≥ 1 (e.g. "6.21 mi").</param>
/// <param name="SmallNumberSigFigs">Significant figures for results where |n| &lt; 1 (e.g. "0.00145 psi").</param>
/// <param name="BasePrecision">Significant figures passed to math.format() before rounding.</param>
public record FormatConfig(
    int LargeNumberDecimals = 2,
    int SmallNumberSigFigs  = 3,
    int BasePrecision       = 10);

/// <summary>
/// Wraps a Jint engine loaded with math.js (embedded resource).
/// Initialization runs on a background thread so the app startup is not blocked.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class MathJsEngine : IDisposable {
    private readonly Lock _lock = new();
    private readonly ICurrencyRateProvider _currencyRates;
    private readonly FormatConfig _formatConfig;
    private volatile Engine? _engine;

    private readonly Task _initTask;

    // Tracks rates registered in the JS engine; used to detect stale registrations without
    // calling math.createUnit unnecessarily (repeated override calls can corrupt math.js state).
    // Keyed by currency, value is the formatted string sent to JS (avoids float equality warnings).
    private readonly Dictionary<string, string> _registeredRates = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> _inputAliases = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _displayNames = new Dictionary<string, string>();
    private HashSet<string> _normalizeUnits = [];

    private record UnitConfig(
        [property: JsonPropertyName("inputAliases")]        Dictionary<string, string>  InputAliases,
        [property: JsonPropertyName("tokenAliases")]        Dictionary<string, string>  TokenAliases,
        [property: JsonPropertyName("ambiguityOverrides")]  Dictionary<string, string>? AmbiguityOverrides,
        [property: JsonPropertyName("displayNames")]        Dictionary<string, string>  DisplayNames,
        [property: JsonPropertyName("longNames")]           Dictionary<string, string>? LongNames,
        [property: JsonPropertyName("defaultTargets")]      Dictionary<string, string>  DefaultTargets,
        [property: JsonPropertyName("blocked")]             List<string>                Blocked,
        [property: JsonPropertyName("normalizeUnits")]      List<string>?               NormalizeUnits);

    public MathJsEngine(ICurrencyRateProvider currencyRates, FormatConfig? formatConfig = null) {
        _currencyRates = currencyRates;
        _formatConfig = formatConfig ?? new FormatConfig();
        _initTask = Task.Run(Initialize);
    }

    private void Initialize() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.math.min.js"));
        // Inject format constants before mathjs-helpers.js so smartFormat can use them.
        engine.SetValue("_FMT_LARGE_DECIMALS", _formatConfig.LargeNumberDecimals);
        engine.SetValue("_FMT_SMALL_SIG_FIGS",  _formatConfig.SmallNumberSigFigs);
        engine.SetValue("_FMT_BASE_PRECISION",   _formatConfig.BasePrecision);
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.mathjs-helpers.js"));

        // Inject pre-computed maps — required. If the resource is missing, the app cannot start.
        // Regenerate with: MATHJS_UPDATE_SNAPSHOT=1 dotnet test --project Yottacast.Core.Tests
        var precomputedJson = LoadPrecomputedResource();
        engine.SetValue("_precomputedJson", precomputedJson);
        engine.Execute("loadPrecomputedData(JSON.parse(_precomputedJson));");

        // Load alias/blocked configuration from unit-config.json
        var aliasJson = LoadResource("Yottacast.Core.Search.Calculator.unit-config.json");
        var aliasData = JsonSerializer.Deserialize<UnitConfig>(aliasJson)!;
        _inputAliases = aliasData.InputAliases;
        _displayNames = aliasData.DisplayNames;
        _normalizeUnits = new HashSet<string>(
            aliasData.NormalizeUnits ?? [],
            StringComparer.OrdinalIgnoreCase);
        engine.SetValue("_aliasJson", aliasJson);
        engine.Execute("loadAliasData(JSON.parse(_aliasJson));");

        // math.createUnit('USD') in mathjs-helpers.js already triggers math.js unit system
        // initialization, which serves as the JIT warmup for subsequent evaluations.

        lock (_lock) {
            _engine = engine;
        }
    }

    private static string LoadPrecomputedResource() {
        using var stream = typeof(MathJsEngine).Assembly
            .GetManifestResourceStream("Yottacast.Core.Search.Calculator.mathjs-precomputed.json")
            ?? throw new InvalidOperationException(
                "Embedded resource 'mathjs-precomputed.json' not found. " +
                "Regenerate with: MATHJS_UPDATE_SNAPSHOT=1 dotnet test --project Yottacast.Core.Tests");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public Task WhenReady() => _initTask;

    /// <summary>
    /// Normalizes a math expression using math.js: cleans the AST, fixes unit/function casing,
    /// detects ambiguous tokens, and determines the expression kind (calculation, unit_entry,
    /// simple_conversion, complex_conversion). Returns null for function definitions.
    /// </summary>
    public NormalizedExpression? NormalizeExpression(string expression) {
        if (_engine == null) throw new InvalidOperationException("Engine not ready");
        lock (_lock) {
            if (_engine == null) throw new InvalidOperationException("Engine not ready");
            var cachedRates = _currencyRates.CachedRates;
            var knownCsv = BuildKnownCsv(cachedRates);
            return NormalizeExpressionCore(expression, knownCsv);
        }
    }

    /// <summary>
    /// Evaluates a math expression using math.js.
    /// Returns a CalcResult, ConversionResult, or ErrorResult.
    /// Currency rates are always refreshed from <see cref="ICurrencyRateProvider.CachedRates"/>
    /// on each call so rate updates take effect immediately without restarting the engine.
    /// </summary>
    public EvalResult Evaluate(string expression) {
        if (_engine == null) return new ErrorResult("Engine not ready");
        lock (_lock) {
            if (_engine == null) return new ErrorResult("Engine not ready");
            var cachedRates = _currencyRates.CachedRates;
            var knownCsv = BuildKnownCsv(cachedRates);
            try {
                var normalized = NormalizeExpressionCore(expression, knownCsv);
                if (normalized == null) return new ErrorResult();

                // Register currencies whose rates are new or have changed
                foreach (var currency in normalized.Currencies) {
                    if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!cachedRates.TryGetValue(currency, out var rate)) continue;
                    var rateStr = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (_registeredRates.TryGetValue(currency, out var existing) && existing == rateStr) continue;
                    _engine.Evaluate($"registerCurrency('{currency}', {rateStr})");
                    _registeredRates[currency] = rateStr;
                }

                IReadOnlyList<AmbiguityHint>? hints = normalized.Ambiguities.Count > 0 ? normalized.Ambiguities : null;

                if (normalized.Kind == ExprKind.ComplexConversion) {
                    return EvaluateComplex(normalized, hints);
                }
                return EvaluateSimple(normalized, hints);

            } catch (Exception ex) {
                var (errorKind, errorToken) = ClassifyError(ex.Message);
                return new ErrorResult(ex.Message, errorKind, errorToken) {
                    NormalizedQuery = expression
                };
            }
        }
    }

    private EvalResult EvaluateSimple(NormalizedExpression normalized, IReadOnlyList<AmbiguityHint>? hints) {
        // Normalize intercept: decompose into natural multi-unit representation when interesting
        if (normalized.Kind == ExprKind.UnitEntry
            && normalized.FromUnit != null
            && IsNormalizableUnit(normalized.FromUnit)) {
            var normResult = TryNormalize(normalized, hints);
            if (normResult != null) return normResult;
        }

        var result = EvalJs(normalized.Expr);
        if (result == null) return new ErrorResult() { NormalizedQuery = normalized.Expr, AmbiguityHints = hints };

        if (normalized.Kind == ExprKind.Calculation) {
            return new CalcResult(result) { NormalizedQuery = normalized.Expr, AmbiguityHints = hints };
        }

        // UnitEntry or SimpleConversion → ConversionResult
        var toIdx = normalized.Expr.LastIndexOf(" to ", StringComparison.Ordinal);
        var lhsExpr = toIdx >= 0 ? normalized.Expr[..toIdx] : normalized.Expr;

        // For compound units (e.g. "km / h"), math.js may auto-simplify the result to a custom
        // unit (e.g. "mph"). Always force the LHS to the user's original unit to get the original from.
        var isCompound = normalized.Kind == ExprKind.UnitEntry
                         && normalized.FromUnit?.Contains('/') == true;

        // Original from: force the user's unit — preserves the magnitude as written (e.g. "0.001 V")
        var origLhsResult = normalized.FromUnit != null
            ? EvalJs($"{lhsExpr} to {normalized.FromUnit}")
            : EvalJs(lhsExpr);
        var (fromValue, fromUnit) = origLhsResult != null
            ? SplitValueUnit(origLhsResult)
            : ("", normalized.FromUnit ?? "");

        // Normalized from: let math.js choose the unit (e.g. "1 mV"). Only differs for non-compound
        // SI units when the coefficient is < 1. Null when the result would be the same as the original.
        string? normFromValue = null, normFromUnit = null;
        if (!isCompound && normalized.FromUnit != null) {
            var normLhsResult = EvalJs(lhsExpr);
            if (normLhsResult != null) {
                var (nfv, nfu) = SplitValueUnit(normLhsResult);
                if (nfu != fromUnit) {
                    normFromValue = nfv;
                    normFromUnit = nfu;
                }
            }
        }

        var (toValue, toUnit) = SplitValueUnit(result);
        if (isCompound) toUnit = normalized.ToUnit ?? toUnit;
        return new ConversionResult(fromValue, fromUnit, toValue, toUnit,
            FromUnitLong:    GetUnitLongName(fromUnit),
            ToUnitLong:      GetUnitLongName(toUnit),
            IsExplicitConversion: normalized.Kind == ExprKind.SimpleConversion,
            FromWasNormalized:    normFromUnit != null,
            NormFromValue:        normFromValue,
            NormFromUnit:         normFromUnit,
            NormFromUnitLong:     normFromUnit != null ? GetUnitLongName(normFromUnit) : null) {
            NormalizedQuery = normalized.Expr,
            AmbiguityHints = hints
        };
    }

    private EvalResult EvaluateComplex(NormalizedExpression normalized, IReadOnlyList<AmbiguityHint>? hints) {
        // For compound FROM units (e.g. "mi / s"), force display in the declared unit to prevent
        // math.js from auto-simplifying to a custom unit (e.g. "kmh") registered in the same dimension.
        bool isCompoundLeft = normalized.FromUnit?.Contains('/') == true;

        // Original from: force the user's unit to preserve the magnitude as written
        var origLeftResult = normalized.FromUnit != null
            ? EvalJs($"{normalized.LeftExpr!} to {normalized.FromUnit}")
            : EvalJs(normalized.LeftExpr!);
        var (fromValue, fromUnit) = origLeftResult != null
            ? SplitValueUnit(origLeftResult)
            : ("", normalized.FromUnit ?? "");

        // Normalized from: let math.js choose (only differs for non-compound SI with coefficient < 1)
        string? normFromValue = null, normFromUnit = null;
        if (!isCompoundLeft && normalized.FromUnit != null) {
            var normLeftResult = EvalJs(normalized.LeftExpr!);
            if (normLeftResult != null) {
                var (nfv, nfu) = SplitValueUnit(normLeftResult);
                if (nfu != fromUnit) {
                    normFromValue = nfv;
                    normFromUnit = nfu;
                }
            }
        }

        var fullResult = EvalJs(normalized.Expr);
        if (fullResult == null) return new ErrorResult() { NormalizedQuery = normalized.Expr, AmbiguityHints = hints };
        var (toValue, toUnit) = SplitValueUnit(fullResult);
        return new ConversionResult(fromValue, fromUnit, toValue, toUnit,
            FromUnitLong:    GetUnitLongName(fromUnit),
            ToUnitLong:      GetUnitLongName(toUnit),
            IsExplicitConversion: true,
            FromWasNormalized:    normFromUnit != null,
            NormFromValue:        normFromValue,
            NormFromUnit:         normFromUnit,
            NormFromUnitLong:     normFromUnit != null ? GetUnitLongName(normFromUnit) : null) {
            NormalizedQuery = normalized.Expr,
            AmbiguityHints = hints
        };
    }

    private string? GetUnitLongName(string symbol) {
        if (string.IsNullOrEmpty(symbol)) return null;
        // Check explicit overrides first (longNames in unit-config.json).
        // These are valid even when the name equals the symbol (e.g. "day"→"day") because
        // LongForm still produces a useful plural ("10 days" ≠ "10 day").
        var explicit_ = _engine!.Evaluate($"getExplicitLongName('{Escape(symbol)}')").ToString();
        if (!string.IsNullOrEmpty(explicit_)) return explicit_;
        // For compound units (e.g. "km / h"), derive long name from components rather than
        // requiring an explicit entry for every possible combination.
        var slashIdx = symbol.IndexOf(" / ", StringComparison.Ordinal);
        if (slashIdx >= 0) {
            var num = symbol[..slashIdx];
            var den = symbol[(slashIdx + 3)..];
            var numLong = GetComponentLongName(num);
            var denLong = GetComponentLongName(den);
            if (numLong != null || denLong != null)
                return $"{numLong ?? num} per {denLong ?? den}";
        }
        // Fall back to math.js LONG-prefix derivation; discard if it only echoes the symbol.
        var derived = _engine.Evaluate($"getUnitLongName('{Escape(symbol)}')").ToString();
        return string.IsNullOrEmpty(derived) || derived == symbol ? null : derived;
    }

    private string? GetComponentLongName(string symbol) {
        var explicit_ = _engine!.Evaluate($"getExplicitLongName('{Escape(symbol)}')").ToString();
        if (!string.IsNullOrEmpty(explicit_)) return explicit_;
        var derived = _engine.Evaluate($"getUnitLongName('{Escape(symbol)}')").ToString();
        return string.IsNullOrEmpty(derived) || derived == symbol ? null : derived;
    }

    private static string BuildKnownCsv(IReadOnlyDictionary<string, double> rates) =>
        string.Join(",", rates.Keys.Select(k => k.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase));

    private (CalcErrorKind Kind, string? Token) ClassifyError(string errorMessage) {
        try {
            var json = _engine!.Evaluate(
                $"JSON.stringify(classifyError('{Escape(errorMessage)}'))").ToString();
            return ParseErrorClassification(json);
        } catch {
            return (CalcErrorKind.Other, null);
        }
    }

    private static (CalcErrorKind Kind, string? Token) ParseErrorClassification(string json) {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var kind = root.GetProperty("type").GetString() switch {
            "unknown_symbol"    => CalcErrorKind.UnknownSymbol,
            "incompatible_units"=> CalcErrorKind.IncompatibleUnits,
            "syntax"            => CalcErrorKind.Syntax,
            _                   => CalcErrorKind.Other
        };

        string? token = null;
        if (root.TryGetProperty("token", out var tokenEl) && tokenEl.ValueKind != JsonValueKind.Null)
            token = tokenEl.GetString();

        return (kind, token);
    }

    private static List<AmbiguityHint> ParseAmbiguityHints(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var list = new List<AmbiguityHint>();
            foreach (var hintEl in doc.RootElement.EnumerateArray()) {
                var input = hintEl.GetProperty("input").GetString() ?? "";
                var variants = new List<UnitVariant>();
                foreach (var c in hintEl.GetProperty("candidates").EnumerateArray()) {
                    var sym = c.GetProperty("symbol").GetString() ?? "";
                    var longName = c.GetProperty("longName").GetString() ?? sym;
                    variants.Add(new UnitVariant(sym, longName));
                }
                list.Add(new AmbiguityHint(input, variants));
            }
            return list;
        } catch {
            return [];
        }
    }

    /// <summary>Returns the display-friendly name for a unit symbol (e.g. "degC" → "°C").</summary>
    public string DisplayUnit(string unit) =>
        _displayNames.TryGetValue(unit, out var display) ? display : unit;

    private NormalizedExpression? NormalizeExpressionCore(string expression, string knownCsv) {
        // Apply special-char input aliases (e.g., "°c" → "degC") before parsing
        foreach (var (alias, canonical) in _inputAliases)
            expression = Regex.Replace(expression, Regex.Escape(alias), canonical,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var escaped = Escape(expression);
        var raw = _engine!.Evaluate(
            $"(function(){{ var r = normalizeExpression('{escaped}', '{knownCsv}'); " +
            $"if (r === null) return null; " +
            $"return {{expr: r.expr, kind: r.kind, fromUnit: r.fromUnit || null, toUnit: r.toUnit || null, leftExpr: r.leftExpr || null, currencies: r.currencies, ambigJson: JSON.stringify(r.ambiguities)}}; }})()");

        if (raw.IsNull() || raw.IsUndefined()) return null;
        var jsObj = raw.AsObject();

        var kind = jsObj.Get("kind").AsString() switch {
            "unit_entry"         => ExprKind.UnitEntry,
            "simple_conversion"  => ExprKind.SimpleConversion,
            "complex_conversion" => ExprKind.ComplexConversion,
            _                    => ExprKind.Calculation
        };

        var fromUnitVal = jsObj.Get("fromUnit");
        var toUnitVal   = jsObj.Get("toUnit");
        var leftExprVal = jsObj.Get("leftExpr");

        return new NormalizedExpression(
            Expr:        jsObj.Get("expr").AsString(),
            Kind:        kind,
            FromUnit:    fromUnitVal.IsNull() || fromUnitVal.IsUndefined() ? null : fromUnitVal.AsString(),
            ToUnit:      toUnitVal.IsNull()   || toUnitVal.IsUndefined()   ? null : toUnitVal.AsString(),
            LeftExpr:    leftExprVal.IsNull()  || leftExprVal.IsUndefined()  ? null : leftExprVal.AsString(),
            Currencies:  jsObj.Get("currencies").AsArray().Select(x => x.AsString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Ambiguities: ParseAmbiguityHints(jsObj.Get("ambigJson").AsString())
        );
    }

    // smartFormat applies smart decimal precision: 2 d.p. for numbers >= 1, full 10 sig-figs for small numbers.
    private string? EvalJs(string expr) {
        var js = $"(function(){{ var r = math.evaluate('{Escape(expr)}'); return smartFormat(r); }})()";
        var result = _engine!.Evaluate(js).ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    // Splits "9.2 EUR" → ("9.2", "EUR"), "42" → ("42", "")
    private static (string Value, string Unit) SplitValueUnit(string s) {
        var idx = s.IndexOf(' ');
        return idx < 0 ? (s, "") : (s[..idx], s[(idx + 1)..]);
    }

    private static string Escape(string s) => s
        .Replace("\\", @"\\")
        .Replace("'", "\\'")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\0", "");

    private static string LoadResource(string name) {
        using var stream = typeof(MathJsEngine).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource not found: {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private record NormComponent(string Value, string Unit, string Display, string LongName);

    private bool IsNormalizableUnit(string unit) {
        if (_normalizeUnits.Contains(unit)) return true;
        var result = _engine!.Evaluate($"isNormalizableUnit('{Escape(unit)}')").ToString();
        return result == "true";
    }

    private List<NormComponent>? ComputeNormalization(string value, string unit) {
        var json = _engine!.Evaluate(
            $"(function(){{ var r=computeNormalization('{Escape(value)}','{Escape(unit)}'); " +
            $"return r?JSON.stringify(r):null; }})()").ToString();
        if (string.IsNullOrEmpty(json) || json == "null") return null;
        try {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(el => new NormComponent(
                    el.GetProperty("value").GetString()!,
                    el.GetProperty("unit").GetString()!,
                    el.GetProperty("display").GetString()!,
                    el.GetProperty("longName").GetString()!))
                .ToList();
        } catch { return null; }
    }

    private static string FormatNormalizedShort(List<NormComponent> components) =>
        string.Join(" ", components.Select(c => $"{c.Value} {c.Display}"));

    private static string FormatNormalizedLong(List<NormComponent> components) =>
        string.Join(" ", components.Select(c => $"{c.Value} {PluralizeName(c.LongName, c.Value)}"));

    private static string PluralizeName(string name, string valueStr) {
        if (!double.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return name;
        if (Math.Abs(d) == 1.0) return name;
        if (name == "foot")  return "feet";
        if (name == "inch")  return "inches";
        return name.EndsWith('s') || name.EndsWith("heit") ? name : name + "s";
    }

    private ConversionResult? TryNormalize(NormalizedExpression normalized, IReadOnlyList<AmbiguityHint>? hints) {
        var toIdx = normalized.Expr.LastIndexOf(" to ", StringComparison.Ordinal);
        var lhsExpr = toIdx >= 0 ? normalized.Expr[..toIdx] : normalized.Expr;
        var origUnit = normalized.FromUnit!;
        // Force result in original unit to prevent math.js SI auto-normalization (e.g. 38000s → 38 ks)
        var origResult = EvalJs($"{lhsExpr} to {origUnit}");
        if (origResult == null) return null;

        var (fromValue, fromUnit) = SplitValueUnit(origResult);
        var components = ComputeNormalization(fromValue, fromUnit);
        if (components == null || components.Count == 0) return null;

        bool isInteresting = components.Count > 1 || components[0].Unit != fromUnit;
        if (!isInteresting) return null;

        if (components.Count == 1) {
            return new ConversionResult(fromValue, fromUnit, components[0].Value, components[0].Unit,
                FromUnitLong: GetUnitLongName(fromUnit),
                ToUnitLong:   GetUnitLongName(components[0].Unit),
                IsExplicitConversion: false,
                FromWasNormalized: false,
                NormFromValue: null, NormFromUnit: null, NormFromUnitLong: null) {
                NormalizedQuery = normalized.Expr,
                AmbiguityHints = hints
            };
        }

        // Multi-component: ToUnit="" and ToUnitLong holds the pre-formatted long string
        var toShort = FormatNormalizedShort(components);
        var toLong  = FormatNormalizedLong(components);
        return new ConversionResult(fromValue, fromUnit, toShort, "",
            FromUnitLong: GetUnitLongName(fromUnit),
            ToUnitLong:   toLong != toShort ? toLong : null,
            IsExplicitConversion: false,
            FromWasNormalized: false,
            NormFromValue: null, NormFromUnit: null, NormFromUnitLong: null) {
            NormalizedQuery = normalized.Expr,
            AmbiguityHints = hints
        };
    }

    public void Dispose() {
        try {
            _initTask.Wait();
        } catch { /* init failed, _engine must be null */
        }
        lock (_lock) {
            _engine?.Dispose();
            _engine = null;
        }
    }
}

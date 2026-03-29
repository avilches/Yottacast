using System.Text.Json;
using Jint;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Wraps a Jint engine loaded with math.js (embedded resource).
/// Initialization runs on a background thread so the app startup is not blocked.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class MathJsEngine : IDisposable {
    private readonly Lock _lock = new();
    private readonly ICurrencyRateProvider _currencyRates;
    private volatile Engine? _engine;

    private readonly Task _initTask;

    // Tracks rates registered in the JS engine; used to detect stale registrations without
    // calling math.createUnit unnecessarily (repeated override calls can corrupt math.js state).
    // Keyed by currency, value is the formatted string sent to JS (avoids float equality warnings).
    private readonly Dictionary<string, string> _registeredRates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Currency used as target when the expression has currency units but no explicit conversion.
    /// </summary>
    public const string DefaultCurrency = "EUR";

    public MathJsEngine(ICurrencyRateProvider currencyRates) {
        _currencyRates = currencyRates;
        _initTask = Task.Run(Initialize);
    }

    private void Initialize() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.math.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.mathjs-helpers.js"));

        // Inject pre-computed maps — required. If the resource is missing, the app cannot start.
        // Regenerate with: MATHJS_UPDATE_SNAPSHOT=1 dotnet test --project Yottacast.Core.Tests
        var precomputedJson = LoadPrecomputedResource();
        engine.SetValue("_precomputedJson", precomputedJson);
        engine.Execute("loadPrecomputedData(JSON.parse(_precomputedJson));");

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
    /// Evaluates a math expression using math.js.
    /// If the expression uses known currency units but the AST contains no <c>to</c> conversion node,
    /// automatically appends "to <see cref="DefaultCurrency"/>".
    /// Currency rates are always refreshed from <see cref="ICurrencyRateProvider.CachedRates"/>
    /// on each call so rate updates take effect immediately without restarting the engine.
    /// </summary>
    public EvaluationResult Evaluate(string expression) {
        if (_engine == null) return new EvaluationResult(expression, null, "Engine not ready");
        lock (_lock) {
            if (_engine == null) return new EvaluationResult(expression, null, "Engine not ready");
            var cachedRates = _currencyRates.CachedRates;
            var knownCsv = string.Join(",", cachedRates.Keys.Select(k => k.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase));
            try {
                // Parse in JS, normalize currency/unit/function casing in the AST, append default currency target
                // if currencies are found but no 'to' conversion exists, and return
                // { expr, isConversion, currencies, ambiguities }.
                // Throws on invalid syntax → caught below → EvaluationResult(null, error) → no result shown.
                var normalized = NormalizeExpression(expression, knownCsv);
                var (exprToEval, isConversion, currenciesInExpr, hints) = normalized;

                // Register currencies whose rates are new or have changed
                foreach (var currency in currenciesInExpr) {
                    if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!cachedRates.TryGetValue(currency, out var rate)) continue;
                    var rateStr = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    // Only call math.createUnit when the rate is new or has changed — repeated
                    // override calls on the same unit can corrupt math.js internal state.
                    if (_registeredRates.TryGetValue(currency, out var existing) && existing == rateStr) continue;
                    _engine.Evaluate($"registerCurrency('{currency}', {rateStr})");
                    _registeredRates[currency] = rateStr;
                }

                // math.format rounds to 10 significant digits to avoid noise like 22.046226218487758
                var js = $"(function(){{ var r = math.evaluate('{Escape(exprToEval)}'); return math.format(r, {{precision: 10}}); }})()";
                var result = _engine.Evaluate(js).ToString();
                if (string.IsNullOrWhiteSpace(result))
                    return new EvaluationResult(exprToEval, null, null);

                return new EvaluationResult(exprToEval, result, null, isConversion, hints.Count > 0 ? hints : null);
            } catch (Exception ex) {
                var (errorKind, errorToken, errorSuggestions) = ClassifyError(ex.Message);
                return new EvaluationResult(expression, null, ex.Message,
                    ErrorKind: errorKind, ErrorToken: errorToken, ErrorSuggestions: errorSuggestions);
            }
        }
    }

    private (CalcErrorKind Kind, string? Token, IReadOnlyList<UnitVariant>? Suggestions) ClassifyError(string errorMessage) {
        try {
            var json = _engine!.Evaluate(
                $"JSON.stringify(classifyError('{Escape(errorMessage)}'))").ToString();
            return ParseErrorClassification(json);
        } catch {
            return (CalcErrorKind.Other, null, null);
        }
    }

    private static (CalcErrorKind Kind, string? Token, IReadOnlyList<UnitVariant>? Suggestions) ParseErrorClassification(string json) {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var kind = root.GetProperty("type").GetString() switch {
            "wrong_unit_casing" => CalcErrorKind.WrongUnitCasing,
            "unknown_symbol" => CalcErrorKind.UnknownSymbol,
            "incompatible_units" => CalcErrorKind.IncompatibleUnits,
            "syntax" => CalcErrorKind.Syntax,
            _ => CalcErrorKind.Other
        };

        string? token = null;
        if (root.TryGetProperty("token", out var tokenEl) && tokenEl.ValueKind != JsonValueKind.Null)
            token = tokenEl.GetString();

        List<UnitVariant>? suggestions = null;
        if (root.TryGetProperty("suggestions", out var sugsEl) && sugsEl.ValueKind == JsonValueKind.Array) {
            suggestions = [];
            foreach (var s in sugsEl.EnumerateArray()) {
                var sym = s.GetProperty("symbol").GetString() ?? "";
                var longName = s.GetProperty("longName").GetString() ?? sym;
                suggestions.Add(new UnitVariant(sym, longName));
            }
        }

        return (kind, token, suggestions is { Count: > 0 } ? suggestions : null);
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

    private record NormalizedExpression(string Expr, bool IsConversion, HashSet<string> Currencies, List<AmbiguityHint> Ambiguities);

    private NormalizedExpression NormalizeExpression(string expression, string knownCsv) {
        var escaped = Escape(expression);
        // normalizeExpression returns a JS object; ambiguities is serialized to JSON for easy C# parsing.
        var obj = _engine!.Evaluate(
                $"(function(){{ var r = normalizeExpression('{escaped}', '{knownCsv}', '{DefaultCurrency}'); " +
                $"return {{expr: r.expr, isConversion: r.isConversion, currencies: r.currencies, ambigJson: JSON.stringify(r.ambiguities)}}; }})()")
            .AsObject();
        return new NormalizedExpression(
            obj.Get("expr").AsString(),
            obj.Get("isConversion").AsBoolean(),
            obj.Get("currencies").AsArray().Select(x => x.AsString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ParseAmbiguityHints(obj.Get("ambigJson").AsString())
        );
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
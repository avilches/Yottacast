using Jint;

namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Wraps a Jint engine loaded with math.js (embedded resource).
/// Initialization runs on a background thread so the app startup is not blocked.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class  MathJsEngine : IDisposable {
    private readonly Lock _lock = new();
    private readonly ICurrencyRateProvider _currencyRates;
    private Engine? _engine;
    private readonly Task _initTask;
    // Tracks rates registered in the JS engine; used to detect stale registrations without
    // calling math.createUnit unnecessarily (repeated override calls can corrupt math.js state).
    private readonly Dictionary<string, double> _registeredRates = new(StringComparer.OrdinalIgnoreCase);

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
        var asm = typeof(MathJsEngine).Assembly;
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.math.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.mathjs-helpers.js"));

        engine.Evaluate("math.evaluate('1+1')"); // warmup: trigger JIT so first real call is instant
        lock (_lock) {
            _engine = engine;
        }
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
        if (_engine == null) return new EvaluationResult(null, "Engine not ready");
        lock (_lock) {
            if (_engine == null) return new EvaluationResult(null, "Engine not ready");
            try {
                var cachedRates = _currencyRates.CachedRates;
                var knownCsv = string.Join(",", cachedRates.Keys.Select(k => k.ToUpperInvariant()).Append("USD"));

                // Parse in JS, normalize currency/unit/function casing in the AST, append default currency target
                // if currencies are found but no 'to' conversion exists, and return { expr, hasConversion, currencies }.
                // Throws on invalid syntax → caught below → EvaluationResult(null, error) → no result shown.
                var normalized = _engine.Evaluate($"normalizeExpression('{Escape(expression)}', '{knownCsv}', '{DefaultCurrency}')")
                    .AsObject();

                var exprToEval = normalized.Get("expr").AsString();
                var isConversion = normalized.Get("hasConversion").AsBoolean();
                var currenciesInExpr = normalized.Get("currencies").AsArray()
                    .Select(x => x.AsString())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Register currencies whose rates are new or have changed
                foreach (var currency in currenciesInExpr) {
                    if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!cachedRates.TryGetValue(currency, out var rate)) continue;
                    // Only call math.createUnit when the rate is new or has changed — repeated
                    // override calls on the same unit can corrupt math.js internal state.
                    if (_registeredRates.TryGetValue(currency, out var existing) && existing == rate) continue;
                    _engine.Evaluate($"registerCurrency('{currency}', {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                    _registeredRates[currency] = rate;
                }

                // math.format rounds to 10 significant digits to avoid noise like 22.046226218487758
                var js = $"(function(){{ var r = math.evaluate('{Escape(exprToEval)}'); return math.format(r, {{precision: 10}}); }})()";
                var result = _engine.Evaluate(js).ToString();
                return string.IsNullOrWhiteSpace(result)
                    ? new EvaluationResult(null, null)
                    : new EvaluationResult(result, null, isConversion);
            } catch (Exception ex) {
                return new EvaluationResult(null, ex.Message);
            }
        }
    }

    /// <summary>
    /// Returns a pretty-printed JSON snapshot of the math.js unit registry and the derived
    /// token map. Used by MathJsUnitSnapshotTests to detect changes when upgrading math.js.
    /// </summary>
    public string ExtractUnitSnapshot() {
        lock (_lock) {
            if (_engine == null) throw new InvalidOperationException("Engine not ready");
            return _engine.Evaluate("JSON.stringify(extractUnitSnapshot(), null, 2)").ToString();
        }
    }

    private static string Escape(string s) => s.Replace("\\", @"\\").Replace("'", "\\'");

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
using Jint;

namespace Yottacast.Core.Services;

/// <summary>
/// Wraps a Jint engine loaded with math.js (embedded resource).
/// Initialization runs on a background thread so the app startup is not blocked.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class MathJsEngine : IDisposable {
    private readonly object _lock = new();
    private Engine? _engine;
    private readonly Task _initTask;

    public MathJsEngine() {
        _initTask = Task.Run(Initialize);
    }

    private void Initialize() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        var asm = typeof(MathJsEngine).Assembly;
        const string resourceName = "Yottacast.Core.Scripts.math.min.js";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}. Run 'dotnet build' to download math.js first.");
        using var reader = new StreamReader(stream);
        engine.Execute(reader.ReadToEnd());
        engine.Evaluate("math.evaluate('1+1')"); // warmup: trigger JIT so first real call is instant
        lock (_lock) {
            _engine = engine;
        }
    }

    public Task WhenReady() => _initTask;

    /// <summary>
    /// Evaluates a math expression using math.js. Returns null if the engine is not yet
    /// initialized or if the expression is invalid / produces no result.
    /// </summary>
    public string? Evaluate(string expression) {
        if (_engine == null) return null;
        lock (_lock) {
            if (_engine == null) return null;
            try {
                var escaped = expression.Replace("\\", "\\\\").Replace("'", "\\'");
                // math.format rounds to 10 significant digits to avoid noise like 22.046226218487758
                var js = $"(function(){{ var r = math.evaluate('{escaped}'); return math.format(r, {{precision: 10}}); }})()";
                var result = _engine.Evaluate(js).ToString();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            } catch {
                return null;
            }
        }
    }

    public void Dispose() {
        try { _initTask.Wait(); } catch { /* init failed, _engine must be null */ }
        lock (_lock) {
            _engine?.Dispose();
            _engine = null;
        }
    }
}

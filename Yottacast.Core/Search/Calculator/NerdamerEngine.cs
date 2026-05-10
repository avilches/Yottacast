using System.Text.Json;
using System.Text.Json.Serialization;
using Jint;

namespace Yottacast.Core.Search.Calculator;

public record VariableSolution(
    [property: JsonPropertyName("variable")] string Variable,
    [property: JsonPropertyName("solutions")] string[] Solutions);

public record SolveResult(VariableSolution[] Variables);

/// <summary>
/// Wraps a Jint engine loaded with nerdamer (core + Algebra addon).
/// Solves algebraic equations symbolically: "2x-5=2" → x = 3.5.
/// Initializes in background; TrySolve returns null while not ready.
/// Thread-safe: a lock guards the engine during evaluation.
/// </summary>
public sealed class NerdamerEngine : IDisposable {
    private readonly Lock _lock = new();
    private volatile Engine? _engine;
    private readonly Task _initTask;

    public NerdamerEngine() {
        _initTask = Task.Run(Initialize);
    }

    private void Initialize() {
        var engine = new Engine(opts => opts.LimitRecursion(64));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.nerdamer.core.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.Algebra.min.js"));
        engine.Execute(LoadResource("Yottacast.Core.Search.Calculator.nerdamer-helpers.js"));
        lock (_lock) {
            _engine = engine;
        }
    }

    public Task WhenReady() => _initTask;

    /// <summary>
    /// Solves the equation in <paramref name="query"/> (must contain '=').
    /// Returns null if the engine is not ready, the query has no variables,
    /// all solutions are trivial, or nerdamer throws.
    /// Thread-safe.
    /// </summary>
    public SolveResult? TrySolve(string query) {
        if (_engine == null) return null;
        lock (_lock) {
            if (_engine == null) return null;
            try {
                var json = _engine.Evaluate($"solveEquation({JsonSerializer.Serialize(query)})");
                if (json.IsNull() || json.IsUndefined()) return null;
                var jsonStr = json.AsString();
                if (string.IsNullOrEmpty(jsonStr)) return null;
                var vars = JsonSerializer.Deserialize<VariableSolution[]>(jsonStr);
                if (vars == null || vars.Length == 0) return null;
                return new SolveResult(vars);
            } catch {
                return null;
            }
        }
    }

    private static string LoadResource(string name) {
        using var stream = typeof(NerdamerEngine).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource not found: {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() {
        lock (_lock) {
            _engine = null;
        }
    }
}
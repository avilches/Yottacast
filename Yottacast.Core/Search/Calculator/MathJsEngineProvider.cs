namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Holds the current <see cref="MathJsEngine"/> and supports atomic hot-swap when
/// exchange rates change. Consumers always read <see cref="Current"/> — never cache the instance.
/// </summary>
public sealed class MathJsEngineProvider : IDisposable {
    private volatile MathJsEngine? _current;
    private readonly SemaphoreSlim _recreateLock = new(1, 1);
    private readonly bool _ownsEngine;

    public MathJsEngineProvider() {
        _ownsEngine = true;
    }

    private MathJsEngineProvider(MathJsEngine engine) {
        _current = engine;
        _ownsEngine = false; // caller owns the engine lifetime
    }

    /// <summary>
    /// Creates a provider pre-seeded with an already-initialized engine.
    /// The provider does NOT dispose the engine when it is disposed — the caller retains ownership.
    /// Use only in tests where the engine is managed by a shared fixture.
    /// </summary>
    public static MathJsEngineProvider ForTesting(MathJsEngine engine) => new(engine);

    /// <summary>
    /// The current engine, or null while the first engine is still initializing.
    /// Consumers must handle null gracefully (e.g. return empty results).
    /// </summary>
    public MathJsEngine? Current => _current;

    /// <summary>
    /// Creates a new engine with the given rates and config, waits for it to be ready (~2s),
    /// then atomically swaps it in. The old engine is disposed.
    /// Call from a background Task — this method blocks for engine initialization.
    /// Concurrent calls are serialized via an internal lock.
    /// </summary>
    public async Task RecreateAsync(IReadOnlyDictionary<string, double> rates, FormatConfig config) {
        await _recreateLock.WaitAsync();
        try {
            var next = new MathJsEngine(rates, config);
            await next.WhenReady();
            var old = Interlocked.Exchange(ref _current, next);
            if (_ownsEngine) old?.Dispose();
        } finally {
            _recreateLock.Release();
        }
    }

    public void Dispose() {
        _recreateLock.Dispose();
        var engine = Interlocked.Exchange(ref _current, null);
        if (_ownsEngine) engine?.Dispose();
    }
}

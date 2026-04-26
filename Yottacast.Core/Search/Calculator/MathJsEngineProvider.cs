namespace Yottacast.Core.Search.Calculator;

/// <summary>
/// Holds the current <see cref="MathJsEngine"/> and supports atomic hot-swap when
/// exchange rates change. Consumers always read <see cref="Current"/> — never cache the instance.
/// </summary>
public sealed class MathJsEngineProvider : IDisposable {
    private volatile MathJsEngine? _current;
    private readonly SemaphoreSlim _recreateLock = new(1, 1);

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
            old?.Dispose();
        } finally {
            _recreateLock.Release();
        }
    }

    public void Dispose() {
        _recreateLock.Dispose();
        var engine = Interlocked.Exchange(ref _current, null);
        engine?.Dispose();
    }
}

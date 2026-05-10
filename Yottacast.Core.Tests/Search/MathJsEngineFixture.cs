using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Shared fixture that initializes MathJsEngine once for all math-related test classes.
/// Engine init loads ~700KB of JS; sharing avoids re-parsing per class.
/// </summary>
public sealed class MathJsEngineFixture : IAsyncLifetime {
    public MathJsEngine Engine { get; } = new(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
        ["USD"] = 1.0,
        ["EUR"] = 0.92,
        ["JPY"] = 150.5,
        ["MXN"] = 17.1,
        ["GBP"] = 0.79,
    });

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("MathJs")]
public class MathJsCollection : ICollectionFixture<MathJsEngineFixture>, ICollectionFixture<NerdamerEngineFixture>;

/// <summary>
/// Fixture with standard test rates for currency tests.
/// The engine is immutable; to test different rates, create a new engine in the test.
/// </summary>
public sealed class MathJsEngineWithRatesFixture : IAsyncLifetime {
    public MathJsEngine Engine { get; } = new(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
        ["USD"] = 1.0,
        ["EUR"] = 0.92,
        ["JPY"] = 150.5,
        ["GBP"] = 0.79,
    });

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("MathJsMutableRates")]
public class MathJsMutableRatesCollection : ICollectionFixture<MathJsEngineWithRatesFixture>;

using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Shared fixture that initializes MathJsEngine once for all math-related test classes.
/// Engine init loads ~700KB of JS; sharing avoids re-parsing per class.
/// </summary>
public sealed class MathJsEngineFixture : IAsyncLifetime {
    public MathJsEngine Engine { get; } = new(new StaticCurrencyRateProvider());

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("MathJs")]
public class MathJsCollection : ICollectionFixture<MathJsEngineFixture>;

/// <summary>
/// Fixture that exposes a MutableCurrencyRateProvider so tests can change rates at runtime.
/// Uses a separate engine instance from MathJsEngineFixture to avoid shared state.
/// </summary>
public sealed class MathJsEngineMutableRatesFixture : IAsyncLifetime {
    public MutableCurrencyRateProvider RateProvider { get; } = new([
        new("USD", 1.0),
        new("EUR", 0.92),
        new("JPY", 150.5),
        new("GBP", 0.79),
    ]);

    public MathJsEngine Engine { get; }

    public MathJsEngineMutableRatesFixture() {
        Engine = new MathJsEngine(RateProvider);
    }

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("MathJsMutableRates")]
public class MathJsMutableRatesCollection : ICollectionFixture<MathJsEngineMutableRatesFixture>;

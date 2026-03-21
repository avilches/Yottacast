using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Shared fixture that initializes MathJsEngine once for all math-related test classes.
/// Engine init loads ~700KB of JS; sharing avoids re-parsing per class.
/// </summary>
public sealed class MathJsEngineFixture : IAsyncLifetime {
    public MathJsEngine Engine { get; } = new();

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("MathJs")]
public class MathJsCollection : ICollectionFixture<MathJsEngineFixture>;

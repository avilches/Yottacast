using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search;

/// <summary>
/// Shared fixture that initializes NerdamerEngine once for all equation test classes.
/// Engine init loads nerdamer (~500KB of JS); sharing avoids re-parsing per class.
/// </summary>
public sealed class NerdamerEngineFixture : IAsyncLifetime {
    public NerdamerEngine Engine { get; } = new();

    public Task InitializeAsync() => Engine.WhenReady();

    public Task DisposeAsync() {
        Engine.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("Nerdamer")]
public class NerdamerCollection : ICollectionFixture<NerdamerEngineFixture>;
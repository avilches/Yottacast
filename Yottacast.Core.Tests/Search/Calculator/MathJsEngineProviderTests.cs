using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;

/// <summary>
/// Tests for MathJsEngineProvider. Each test creates its own engine(s) because the
/// provider lifecycle (null → engine → disposed) cannot be shared between tests.
/// These tests are slow (~2s per engine initialization) and run in a dedicated collection
/// so they don't block the shared MathJs fixture.
/// </summary>
[Collection("MathJsProvider")]
public class MathJsEngineProviderTests {

    private static readonly IReadOnlyDictionary<string, double> BaseRates =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
            ["USD"] = 1.0,
            ["EUR"] = 0.92,
        };

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void Current_BeforeRecreate_IsNull() {
        using var provider = new MathJsEngineProvider();

        Assert.Null(provider.Current);
    }

    // ── RecreateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecreateAsync_SetsCurrentEngine() {
        using var provider = new MathJsEngineProvider();

        await provider.RecreateAsync(BaseRates, new FormatConfig());

        Assert.NotNull(provider.Current);
    }

    [Fact]
    public async Task RecreateAsync_WithDifferentRates_ProducesDifferentResults() {
        using var provider = new MathJsEngineProvider();

        // First engine: EUR = 0.5 → 10 USD = 5 EUR
        var rates1 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
            ["USD"] = 1.0, ["EUR"] = 0.5
        };
        await provider.RecreateAsync(rates1, new FormatConfig());
        var result1 = provider.Current!.Evaluate("10 USD to EUR");

        // Second engine: EUR = 2.0 → 10 USD = 20 EUR
        var rates2 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
            ["USD"] = 1.0, ["EUR"] = 2.0
        };
        await provider.RecreateAsync(rates2, new FormatConfig());
        var result2 = provider.Current!.Evaluate("10 USD to EUR");

        Assert.True(result1.IsSuccess, result1.Error);
        Assert.True(result2.IsSuccess, result2.Error);
        Assert.Contains("5", result1.Value!);
        Assert.Contains("20", result2.Value!);
        Assert.NotEqual(result1.Value, result2.Value);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_SetsCurrentToNull() {
        var provider = new MathJsEngineProvider();
        await provider.RecreateAsync(BaseRates, new FormatConfig());
        Assert.NotNull(provider.Current);

        provider.Dispose();

        Assert.Null(provider.Current);
    }
}

[CollectionDefinition("MathJsProvider")]
public class MathJsProviderCollection;

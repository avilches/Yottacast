using Xunit;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJsMutableRates")]
public class CurrencyRateUpdateTests(MathJsEngineMutableRatesFixture fixture) {

    /// <summary>
    /// Verifies that MathJsEngine re-reads the provider's CachedRates on every Evaluate call,
    /// so a rate change is reflected immediately in the next evaluation without restarting the engine.
    /// </summary>
    [Fact]
    public void CurrencyRate_WhenChanged_EvaluationReflectsNewRate() {
        fixture.RateProvider.SetRate("EUR", 0.5); // 1 USD = 0.5 EUR → 10 USD = 5 EUR
        var r1 = fixture.Engine.Evaluate("10 USD to EUR");
        Assert.True(r1.IsSuccess, r1.Error);
        Assert.Contains("5", r1.Value!);

        fixture.RateProvider.SetRate("EUR", 2.0); // 1 USD = 2 EUR → 10 USD = 20 EUR
        var r2 = fixture.Engine.Evaluate("10 USD to EUR");
        Assert.True(r2.IsSuccess, r2.Error);
        Assert.Contains("20", r2.Value!);

        Assert.NotEqual(r1.Value, r2.Value);
    }

    [Fact]
    public void CurrencyRate_WhenUnchanged_ReturnsSameResult() {
        fixture.RateProvider.SetRate("JPY", 150.0); // 1 USD = 150 JPY → 1 USD = 150 JPY
        var r1 = fixture.Engine.Evaluate("1 USD to JPY");
        Assert.True(r1.IsSuccess);

        var r2 = fixture.Engine.Evaluate("1 USD to JPY");
        Assert.True(r2.IsSuccess);

        Assert.Equal(r1.Value, r2.Value);
    }
}
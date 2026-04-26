using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJsMutableRates")]
public class CurrencyRateUpdateTests(MathJsEngineWithRatesFixture fixture) {

    [Fact]
    public void Engine_WithRate_EvaluatesCorrectly() {
        // Engine has EUR=0.92 -> 10 USD = 9.2 EUR
        var r = fixture.Engine.Evaluate("10 USD to EUR");
        Assert.True(r.IsSuccess, r.Error);
        Assert.Contains("9.2", r.Value!);
    }

    [Fact]
    public async Task TwoEngines_WithDifferentRates_ProduceDifferentResults() {
        // Engine 1: EUR=0.5 -> 10 USD = 5 EUR
        var rates1 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
            ["USD"] = 1.0, ["EUR"] = 0.5
        };
        using var engine1 = new MathJsEngine(rates1);
        await engine1.WhenReady();
        var r1 = engine1.Evaluate("10 USD to EUR");

        // Engine 2: EUR=2.0 -> 10 USD = 20 EUR
        var rates2 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
            ["USD"] = 1.0, ["EUR"] = 2.0
        };
        using var engine2 = new MathJsEngine(rates2);
        await engine2.WhenReady();
        var r2 = engine2.Evaluate("10 USD to EUR");

        Assert.True(r1.IsSuccess, r1.Error);
        Assert.True(r2.IsSuccess, r2.Error);
        Assert.Contains("5", r1.Value!);
        Assert.Contains("20", r2.Value!);
        Assert.NotEqual(r1.Value, r2.Value);
    }

    [Fact]
    public void Engine_SameRate_ReturnsConsistentResults() {
        var r1 = fixture.Engine.Evaluate("1 USD to JPY");
        var r2 = fixture.Engine.Evaluate("1 USD to JPY");
        Assert.True(r1.IsSuccess);
        Assert.Equal(r1.Value, r2.Value);
    }
}

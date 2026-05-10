using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("Nerdamer")]
public class EquationSolverTests(NerdamerEngineFixture fixture) {

    // ── NerdamerEngine.TrySolve direct tests ─────────────────────────────────

    [Theory]
    [InlineData("2x-5=2",     "x", "3.5")]
    [InlineData("x+3=7",      "x", "4")]
    [InlineData("3x=9",       "x", "3")]
    public void TrySolve_LinearEquation_ReturnsSolution(string query, string variable, string expected) {
        var result = fixture.Engine.TrySolve(query);
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == variable);
        Assert.Equal(expected, Assert.Single(v.Solutions));
    }

    [Fact]
    public void TrySolve_QuadraticTwoRealSolutions_ReturnsBoth() {
        var result = fixture.Engine.TrySolve("x^2-5*x+6=0");
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == "x");
        Assert.Equal(2, v.Solutions.Length);
        Assert.Contains("2", v.Solutions);
        Assert.Contains("3", v.Solutions);
    }

    [Fact]
    public void TrySolve_QuadraticComplexSolutions_ReturnsBoth() {
        var result = fixture.Engine.TrySolve("x^2=-1");
        Assert.NotNull(result);
        var v = result.Variables.First(v => v.Variable == "x");
        Assert.Equal(2, v.Solutions.Length);
        // Solutions are complex (contain 'i')
        Assert.True(v.Solutions.All(s => s.Contains('i')));
    }

    [Fact]
    public void TrySolve_MultiVariableEquation_ReturnsParametricSolution() {
        var result = fixture.Engine.TrySolve("2*x+3*y=10");
        Assert.NotNull(result);
        // At least one variable solved in terms of the other
        Assert.True(result.Variables.Length >= 1);
        var xSol = result.Variables.FirstOrDefault(v => v.Variable == "x");
        Assert.NotNull(xSol);
        Assert.True(xSol.Solutions.Length > 0);
        // Solution for x contains y (parametric)
        Assert.True(xSol.Solutions.Any(s => s.Contains('y')));
    }

    [Theory]
    [InlineData("1+1=2")]    // no variables
    [InlineData("x=x")]      // trivial solution
    [InlineData("2x-=5")]    // syntax error
    [InlineData("abc")]      // no equals sign
    public void TrySolve_InvalidOrTrivial_ReturnsNull(string query) {
        var result = fixture.Engine.TrySolve(query);
        Assert.Null(result);
    }

    // ── CalculatorSearch integration tests ────────────────────────────────────

    private CalculatorSearch MakeCalcSearch() {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        // math.js engine is null (not needed — equations bypass it entirely)
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);
    }

    [Fact]
    public void CalculatorSearch_EquationQuery_ReturnsCalculatorResult() {
        var search = MakeCalcSearch();
        var results = search.Search("2x-5=2", 5);
        var item = Assert.Single(results);
        var calc = Assert.IsType<CalculatorResultItemViewModel>(item);
        Assert.Equal("x = 3.5", calc.Title);
        Assert.Equal("2x-5=2", calc.Subtitle);
    }

    [Fact]
    public void CalculatorSearch_EquationQuery_ActivateCopiesValue() {
        string? copied = null;
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: text => copied = text, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var provider = new MathJsEngineProvider();
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);

        var results = search.Search("2x-5=2", 5);
        var item = Assert.IsType<CalculatorResultItemViewModel>(Assert.Single(results));
        var enterAction = item.Actions.First(a => a.Hotkey == ActionHotkey.Enter);
        enterAction.Execute();

        Assert.Equal("3.5", copied);
    }

    [Theory]
    [InlineData("1+1=2")]
    [InlineData("x=x")]
    public void CalculatorSearch_NoSolution_ReturnsEmpty(string query) {
        var search = MakeCalcSearch();
        Assert.Empty(search.Search(query, 5));
    }
}
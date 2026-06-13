using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class AlgebraSearchTests(NerdamerEngineFixture fixture, MathJsEngineFixture mathJsFixture) {

    // ── NerdamerEngine.TryAlgebra direct tests ────────────────────────────────

    [Fact]
    public void TryAlgebra_SimplifiableExpression_HasSimplifyCell() {
        var result = fixture.Engine.TryAlgebra("2*x+3*x");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "simplify");
        Assert.NotNull(cell);
        Assert.Equal("5*x", cell.Result);
    }

    [Fact]
    public void TryAlgebra_FactorableExpression_HasFactorCell() {
        var result = fixture.Engine.TryAlgebra("x^2-5*x+6");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "factor");
        Assert.NotNull(cell);
        // nerdamer returns "(-2+x)*(-3+x)" — check both roots are structurally present
        Assert.Contains("-2+x", cell.Result);
        Assert.Contains("-3+x", cell.Result);
    }

    [Fact]
    public void TryAlgebra_Polynomial_HasDerivativeCell() {
        var result = fixture.Engine.TryAlgebra("x^2");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "d/dx");
        Assert.NotNull(cell);
        Assert.Equal("2*x", cell.Result);
    }

    [Fact]
    public void TryAlgebra_SingleVariable_HasIntegralCell() {
        var result = fixture.Engine.TryAlgebra("x^2");
        Assert.NotNull(result);
        var cell = result.Cells.FirstOrDefault(c => c.Label == "∫dx");
        Assert.NotNull(cell);
        Assert.Contains("x^3", cell.Result);
    }

    [Fact]
    public void TryAlgebra_MultiVariable_NoIntegralCell() {
        var result = fixture.Engine.TryAlgebra("x*y+2*x");
        Assert.NotNull(result);
        Assert.DoesNotContain(result.Cells, c => c.Label.StartsWith("∫"));
    }

    [Fact]
    public void TryAlgebra_MultiVariable_HasDerivativePerVariable() {
        var result = fixture.Engine.TryAlgebra("x*y+2*x");
        Assert.NotNull(result);
        Assert.Contains(result.Cells, c => c.Label == "d/dx");
        Assert.Contains(result.Cells, c => c.Label == "d/dy");
    }

    [Fact]
    public void TryAlgebra_NoCells_WhenAllResultsMatchInput() {
        // "2+3" has no variables — nerdamer returns empty variables list
        var result = fixture.Engine.TryAlgebra("2+3");
        Assert.Null(result);
    }

    [Fact]
    public void TryAlgebra_PlainText_ReturnsNull() {
        var result = fixture.Engine.TryAlgebra("hello world");
        Assert.Null(result);
    }

    [Fact]
    public void TryAlgebra_IntegralWithRepeatingDecimal_RoundsToConfiguredPlaces() {
        // ∫(x^2 - 5x + 6)dx — nerdamer returns 0.3333333333333333*x^3 for the x^3/3 term
        var result = fixture.Engine.TryAlgebra("x^2 - 5*x + 6", decimalPlaces: 2);
        Assert.NotNull(result);
        var integralCell = result.Cells.FirstOrDefault(c => c.Label.StartsWith("∫"));
        Assert.NotNull(integralCell);
        Assert.DoesNotContain("0.3333333333", integralCell!.Result);
        Assert.Contains("0.33", integralCell.Result);
    }

    [Fact]
    public void TryAlgebra_WithZeroDecimalPlaces_KeepsExactIntegers() {
        var result = fixture.Engine.TryAlgebra("x^2 + 2*x", decimalPlaces: 0);
        Assert.NotNull(result);
        var dCell = result.Cells.FirstOrDefault(c => c.Label == "d/dx");
        Assert.NotNull(dCell);
        // nerdamer may reorder terms (e.g. "2+2*x") — verify it contains only integers, no decimals
        Assert.DoesNotContain(".", dCell!.Result);
        Assert.Contains("2", dCell.Result);
        Assert.Contains("x", dCell.Result);
    }

    [Fact]
    public void TryAlgebra_DuplicateResults_Deduplicated() {
        var result = fixture.Engine.TryAlgebra("x^2-5*x+6");
        if (result == null) return;
        var values = result.Cells.Select(c => c.Result).ToList();
        Assert.Equal(values.Distinct().Count(), values.Count);
    }

    // ── CalculatorSearch integration tests ───────────────────────────────────

    private CalculatorSearch MakeCalcSearch() {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: _ => { }, read: () => Task.FromResult<string?>(null));
        var settings = TestSettings.LoadIsolated(new FakePlatformProvider([]));
        var provider = MathJsEngineProvider.ForTesting(mathJsFixture.Engine);
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings,
            NullLogger<ExchangeRateService>.Instance);
        return new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);
    }

    [Fact]
    public void CalculatorSearch_AlgebraExpression_ReturnsAlgebraResultViewModel() {
        var search = MakeCalcSearch();
        var results = search.Search("2*x+3*x", 5);
        var item = Assert.Single(results);
        Assert.IsType<AlgebraResultItemViewModel>(item);
    }

    [Fact]
    public void CalculatorSearch_AlgebraExpression_HasExpectedCells() {
        var search = MakeCalcSearch();
        var results = search.Search("2*x+3*x", 5);
        var vm = Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
        Assert.Contains(vm.CellItems, c => c.Label == "simplify");
    }

    [Fact]
    public void CalculatorSearch_AlgebraActivate_CopiesSelectedCellResult() {
        string? copied = null;
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        clipboard.Initialize(copy: text => copied = text, read: () => Task.FromResult<string?>(null));
        var settings = TestSettings.LoadIsolated(new FakePlatformProvider([]));
        var provider = MathJsEngineProvider.ForTesting(mathJsFixture.Engine);
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings,
            NullLogger<ExchangeRateService>.Instance);
        var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings,
            NullLogger<CalculatorSearch>.Instance, fixture.Engine);

        var results = search.Search("2*x+3*x", 5);
        var vm = Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
        var enterAction = vm.Actions.First(a => a.Hotkey == ActionHotkey.Enter);
        enterAction.Execute();

        Assert.NotNull(copied);
        Assert.Equal(vm.CellItems[0].Result, copied);
    }

    [Fact]
    public void CalculatorSearch_PlainText_ReturnsEmpty() {
        var search = MakeCalcSearch();
        Assert.Empty(search.Search("safari to km", 5));
    }

    [Fact]
    public void CalculatorSearch_NumericExpression_NotRoutedToAlgebra() {
        // "2+3" is handled by math.js (returns CalcResult), never reaches TryAlgebra
        var search = MakeCalcSearch();
        var results = search.Search("2+3", 5);
        var item = Assert.Single(results);
        Assert.IsType<CalculatorResultItemViewModel>(item);
    }

    [Theory]
    [InlineData("1p")]
    [InlineData("2x")]
    [InlineData("ax")]
    public void CalculatorSearch_AlgebraQueryShorterThanMinLength_ReturnsEmpty(string query) {
        var search = MakeCalcSearch();
        Assert.Empty(search.Search(query, 5));
    }

    [Fact]
    public void CalculatorSearch_AlgebraAtMinLength_ReturnsResult() {
        var search = MakeCalcSearch();
        var results = search.Search("x+1", 5);
        Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
    }

    [Fact]
    public void CalculatorSearch_AlgebraResult_UsesAlgebraResultScore() {
        var search = MakeCalcSearch();
        var results = search.Search("2*x+3*x", 5);
        var vm = Assert.IsType<AlgebraResultItemViewModel>(Assert.Single(results));
        Assert.Equal(AppDefaults.AlgebraResultScore, vm.Score);
    }
}

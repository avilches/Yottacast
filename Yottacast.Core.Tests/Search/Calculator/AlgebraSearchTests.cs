using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("Nerdamer")]
public class AlgebraSearchTests(NerdamerEngineFixture fixture) {

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
        // nerdamer returns "(-2+x)*(-3+x)" or equivalent reordering of terms
        Assert.Contains("2", cell.Result);
        Assert.Contains("3", cell.Result);
        Assert.Contains("x", cell.Result);
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
    public void TryAlgebra_DuplicateResults_Deduplicated() {
        var result = fixture.Engine.TryAlgebra("x^2-5*x+6");
        if (result == null) return;
        var values = result.Cells.Select(c => c.Result).ToList();
        Assert.Equal(values.Distinct().Count(), values.Count);
    }
}

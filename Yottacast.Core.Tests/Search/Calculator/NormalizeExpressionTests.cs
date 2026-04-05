using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class NormalizeExpressionTests(MathJsEngineFixture fixture) {

    private NormalizedExpression? Normalize(string expression) =>
        fixture.Engine.NormalizeExpression(expression);

    // ── AST cleanup ──────────────────────────────────────────────────────────

    [Fact]
    public void BlockNode_KeepsOnlyFirstStatement() {
        var r = Normalize("10 + 2 ; 2 + 3");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.Calculation, r.Kind);
        Assert.DoesNotContain(";", r.Expr);
    }

    [Fact]
    public void FunctionDefinition_ReturnsNull() {
        Assert.Null(Normalize("f(x) = x + 1"));
    }

    [Fact]
    public void AssignmentNode_IsStripped() {
        var r = Normalize("10 + (a = 2)");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.Calculation, r.Kind);
        Assert.DoesNotContain("=", r.Expr);
    }

    // ── Kind detection ───────────────────────────────────────────────────────

    [Fact]
    public void SimpleArithmetic_IsCalculation() {
        var r = Normalize("2 + 2");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.Calculation, r.Kind);
    }

    [Fact]
    public void ExplicitConversion_IsSimpleConversion() {
        var r = Normalize("5 KG to lb");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.SimpleConversion, r.Kind);
        Assert.Equal("5 kg to lb", r.Expr);
    }

    [Fact]
    public void SinglePhysicalUnit_IsUnitEntry_WithDefaultTarget() {
        var r = Normalize("10 kg");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.UnitEntry, r.Kind);
        Assert.Contains("lb", r.Expr);
        Assert.Empty(r.Ambiguities);
    }

    [Fact]
    public void SingleCurrencyUnit_IsUnitEntry_WithDefaultCurrency() {
        var r = Normalize("10 USD");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.UnitEntry, r.Kind);
        Assert.Contains("EUR", r.Expr);
    }

    [Fact]
    public void ComplexConversion_HasLeftExpr() {
        var r = Normalize("(10 USD + 5 EUR) to MXN");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.ComplexConversion, r.Kind);
        Assert.NotNull(r.LeftExpr);
        Assert.Equal("MXN", r.ToUnit);
    }

    // ── Ambiguity detection ──────────────────────────────────────────────────

    [Fact]
    public void AmbiguousUnit_PopulatesHints() {
        // "gt" is not a canonical form and has no ambiguityOverride → resolves to "Gt" (gigatonne)
        // with ambiguity hint for Gt vs GT (gigatesla)
        var r = Normalize("1 gt");
        Assert.NotNull(r);
        Assert.Single(r.Ambiguities);
        Assert.Equal("gt", r.Ambiguities[0].Input);
        Assert.Contains(r.Ambiguities[0].Candidates, c => c.Symbol == "Gt");
        Assert.Contains(r.Ambiguities[0].Candidates, c => c.Symbol == "GT");
    }

    [Fact]
    public void AmbiguousUnit_WithOverride_ProducesAlternativeHint() {
        // "MG" is not a canonical form; ambiguityOverrides resolves to "mg" (milligram)
        // and produces an ambiguity hint showing the alternative "Mg" (megagram)
        var r = Normalize("1 MG");
        Assert.NotNull(r);
        Assert.Single(r.Ambiguities);
        Assert.Equal("MG", r.Ambiguities[0].Input);
        Assert.Equal("mg", r.Ambiguities[0].Candidates[0].Symbol);  // chosen: milligram
        Assert.Contains(r.Ambiguities[0].Candidates, c => c.Symbol == "Mg");  // alternative: megagram
    }

    [Fact]
    public void ExactCanonicalUnit_NoAmbiguityHint() {
        // "mg" is exactly one of the canonical forms → no ambiguity
        var r = Normalize("1 mg");
        Assert.NotNull(r);
        Assert.Empty(r.Ambiguities);
    }

    [Fact]
    public void LowercaseLitre_IsUnitEntry_WithGallonTarget() {
        // "l" y "L" son sinónimos de litre; debe normalizarse a "L" y buscar el par gallon
        var r = Normalize("1 l");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.UnitEntry, r.Kind);
        Assert.Contains("gallon", r.Expr);   // no debe convertir l→L (trivial)
        Assert.Empty(r.Ambiguities);
    }
}

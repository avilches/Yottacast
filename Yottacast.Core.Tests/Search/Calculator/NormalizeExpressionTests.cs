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

    [Fact]
    public void CompoundVelocity_IsUnitEntry_WithDefaultTarget() {
        var r = Normalize("10 km/h");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.UnitEntry, r.Kind);
        Assert.Equal("km / h", r.FromUnit);
        Assert.Contains("mi / h", r.Expr);
    }

    [Fact]
    public void CompoundVelocity_NoTarget_IsCalculation() {
        // "km / km" no tiene par por defecto → calculation
        var r = Normalize("10 km/km");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.Calculation, r.Kind);
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
    public void ForceAmbiguous_mS_ResolvesToMs_WithHint() {
        // "mS" is an exact canonical (millisiemens) but forceAmbiguous remaps it to "ms" (milliseconds)
        var r = Normalize("10 mS");
        Assert.NotNull(r);
        Assert.Single(r.Ambiguities);
        Assert.Equal("mS", r.Ambiguities[0].Input);
        Assert.Equal("ms", r.Ambiguities[0].Candidates[0].Symbol);   // resolved: milliseconds
        Assert.Contains(r.Ambiguities[0].Candidates, c => c.Symbol == "mS"); // alternative: millisiemens
        Assert.Contains("ms", r.Expr);  // expression uses ms
    }

    [Fact]
    public void ForceAmbiguous_MS_ResolvesToMs_WithHint() {
        // "MS" is an exact canonical (megasiemens) but forceAmbiguous remaps it to "ms"
        var r = Normalize("10 MS");
        Assert.NotNull(r);
        Assert.Single(r.Ambiguities);
        Assert.Equal("MS", r.Ambiguities[0].Input);
        Assert.Equal("ms", r.Ambiguities[0].Candidates[0].Symbol);
        Assert.Contains(r.Ambiguities[0].Candidates, c => c.Symbol == "MS"); // alternative: megasiemens
    }

    [Fact]
    public void ExactCanonical_Ms_IsNotForceAmbiguous() {
        // "Ms" is an exact canonical (megasecond) and is NOT in forceAmbiguous → no ambiguity
        var r = Normalize("10 Ms");
        Assert.NotNull(r);
        Assert.Empty(r.Ambiguities);
        Assert.Contains("Ms", r.Expr);
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

    // ── Hz casing ────────────────────────────────────────────────────────────

    [Fact]
    public void Hz_WithDefaultTarget_IsUnitEntry() {
        // Hz tiene defaultTarget "rpm"; debe detectarse como unit_entry
        var r = Normalize("10 Hz");
        Assert.NotNull(r);
        Assert.Equal(ExprKind.UnitEntry, r.Kind);
        Assert.Equal("Hz", r.FromUnit);
        Assert.Equal("rpm", r.ToUnit);
    }

    [Fact]
    public void THz_CapitalH_And_LowercaseH_NormalizeIdentically() {
        // Documenta que "THz" (H mayúscula) y "Thz" (h minúscula) producen el mismo resultado.
        // Si uno falla y el otro no, hay un problema de resolución de casing en resolveUnitToken.
        var r1 = Normalize("10 THz");
        var r2 = Normalize("10 Thz");
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(ExprKind.UnitEntry, r1!.Kind);
        Assert.Equal(ExprKind.UnitEntry, r2!.Kind);
        Assert.Equal("THz", r1.FromUnit);
        Assert.Equal("THz", r2.FromUnit);
        Assert.Equal(r1.Expr, r2.Expr);   // misma expresión normalizada
    }

    [Fact]
    public void PrefixedHz_HaveDefaultTargets_IsUnitEntry() {
        // kHz, MHz, GHz, THz — todos deben tener defaultTarget y resultar en UnitEntry
        foreach (var (input, expectedFrom, expectedTo) in new[] {
            ("10 kHz", "kHz", "Hz"),
            ("10 MHz", "MHz", "kHz"),
            ("10 GHz", "GHz", "MHz"),
            ("10 THz", "THz", "GHz"),
        }) {
            var r = Normalize(input);
            Assert.NotNull(r);
            Assert.Equal(ExprKind.UnitEntry, r!.Kind);
            Assert.Equal(expectedFrom, r.FromUnit);
            Assert.Equal(expectedTo,   r.ToUnit);
        }
    }

}

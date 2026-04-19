using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;


// Tests the JS classifyError function end-to-end via MathJsEngine.Evaluate,
// checking that ErrorResult.ErrorKind / ErrorToken are
// populated correctly for each error category.

[Collection("MathJs")]
public class ClassifyErrorTests(MathJsEngineFixture fixture) {

    // ── Error kind + token ────────────────────────────────────────────────────
    // "E" is Euler's constant in math.js, not a unit. `to E` passes a number instead
    // of a unit to the conversion operator, producing "Unexpected type of argument"
    // which classifyError doesn't recognise → Other → CalculatorSearch shows nothing.

    public static TheoryData<string, CalcErrorKind, string?> ErrorKindCases => new() {
        // UnknownSymbol: identifier not in the unit token map at all
        { "1 XYZUNIT to g", CalcErrorKind.UnknownSymbol,   "XYZUNIT" },
        { "10 USD to U",    CalcErrorKind.UnknownSymbol,   "U"       },
        { "10 USD to X",    CalcErrorKind.UnknownSymbol,   "X"       },
        // IncompatibleUnitsConvert: explicit "X to Y" conversion between incompatible dimensions
        { "1 kg to meter",  CalcErrorKind.IncompatibleUnitsConvert, "kilogram|meter" },
        { "10 km to L",     CalcErrorKind.IncompatibleUnitsConvert, "kilometer|litre" },
        // IncompatibleUnitsOp: arithmetic between incompatible units
        { "1 km + 2 L",     CalcErrorKind.IncompatibleUnitsOp, null },
        // Syntax: parse/syntax error
        { "1 +",            CalcErrorKind.Syntax,           null     },
        // Other: E = Euler's constant → "Unexpected type" → not matched by classifyError patterns
        { "10 USD to E",    CalcErrorKind.Other,            null     },
    };

    [Theory, MemberData(nameof(ErrorKindCases))]
    public void Error_ClassifiesKindAndToken(string expression, CalcErrorKind expectedKind, string? expectedToken) {
        var r = fixture.Engine.Evaluate(expression);
        Assert.False(r.IsSuccess);
        var err = Assert.IsType<ErrorResult>(r);
        Assert.Equal(expectedKind, err.ErrorKind);
        if (expectedToken is not null) Assert.Equal(expectedToken, err.ErrorToken);
    }

    [Theory]
    [InlineData("10 km to L",        "Can't convert kilometer to litre")]
    [InlineData("1 kg to meter",      "Can't convert kilogram to meter")]
    public void IncompatibleUnitsConvert_TokenContainsLongNames(string expression, string expectedToken) {
        var err = Assert.IsType<ErrorResult>(fixture.Engine.Evaluate(expression));
        Assert.Equal(CalcErrorKind.IncompatibleUnitsConvert, err.ErrorKind);
        // Token format is "fromLong|toLong"; verify round-trip matches the expected hint text
        Assert.NotNull(err.ErrorToken);
        var parts = err.ErrorToken!.Split('|');
        Assert.Equal(2, parts.Length);
        Assert.Equal(expectedToken, $"Can't convert {parts[0]} to {parts[1]}");
    }

    // ── MG resolves via ambiguityOverrides to mg (milligram), shows alternative hint ─

    [Fact]
    public void AmbiguousCasing_MG_ResolvesToMilligram_WithAlternativeHint() {
        // "MG" is not an exact canonical; ambiguityOverrides maps "mg" → "mg" (milligram)
        var r = fixture.Engine.Evaluate("5 MG to g");
        Assert.True(r.IsSuccess, r.Error);
        var conv = Assert.IsType<ConversionResult>(r);
        Assert.Equal("mg", conv.FromUnit);   // milligram (not Mg megagram)
        Assert.Equal("0.005", conv.ToValue); // 5 mg = 0.005 g
        Assert.NotNull(r.AmbiguityHints);    // override shows the alternative
        Assert.Contains(r.AmbiguityHints, h => h.Input == "MG");
    }

    // ── Success ───────────────────────────────────────────────────────────────

    [Fact]
    public void SuccessfulEval_IsNotError() {
        var r = fixture.Engine.Evaluate("2 + 2");
        Assert.True(r.IsSuccess);
        Assert.IsNotType<ErrorResult>(r);
    }
}

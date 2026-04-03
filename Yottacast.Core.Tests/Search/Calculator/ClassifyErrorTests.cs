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
        // IncompatibleUnits: both valid units but incompatible dimensions
        { "1 kg to meter",  CalcErrorKind.IncompatibleUnits, null    },
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

    // ── MG resolves to Mg (success + ambiguity hint, not an error) ────────────

    [Fact]
    public void AmbiguousCasing_ResolvesSuccessfully_WithHint() {
        var r = fixture.Engine.Evaluate("5 MG to g");
        Assert.True(r.IsSuccess, r.Error);
        Assert.NotNull(r.AmbiguityHints);
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

using Xunit;
using Yottacast.Core.Search.Calculator;

namespace Yottacast.Core.Tests.Search.Calculator;


// Tests the JS classifyError function end-to-end via MathJsEngine.Evaluate,
// checking that EvaluationResult.ErrorKind / ErrorToken / ErrorSuggestions are
// populated correctly for each error category.

[Collection("MathJs")]
public class ClassifyErrorTests(MathJsEngineFixture fixture) {

    // ── Error kind + token ────────────────────────────────────────────────────
    // "E" is Euler's constant in math.js, not a unit. `to E` passes a number instead
    // of a unit to the conversion operator, producing "Unexpected type of argument"
    // which classifyError doesn't recognise → Other → CalculatorSearch shows nothing.

    public static TheoryData<string, CalcErrorKind, string?> ErrorKindCases => new() {
        // WrongUnitCasing: token whose lowercase maps to >1 canonical form; input is none of them
        { "5 MG to g",      CalcErrorKind.WrongUnitCasing,  "MG"      },
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
        Assert.Equal(expectedKind, r.ErrorKind);
        if (expectedToken is not null) Assert.Equal(expectedToken, r.ErrorToken);
    }

    // ── WrongUnitCasing → suggestions ─────────────────────────────────────────

    [Fact]
    public void WrongUnitCasing_PopulatesSuggestions() {
        var r = fixture.Engine.Evaluate("5 MG to g");
        Assert.NotNull(r.ErrorSuggestions);
        Assert.Contains(r.ErrorSuggestions, s => s.Symbol == "Mg");
        Assert.Contains(r.ErrorSuggestions, s => s.Symbol == "mg");
    }

    // Long names populated in _unitLongNameCache (e.g. "mg" → "milligram")
    public static TheoryData<string, string, string> LongNameCases => new() {
        { "5 MG to g", "mg", "milligram" },
        { "5 MG to g", "Mg", "megagram"  },
    };

    [Theory, MemberData(nameof(LongNameCases))]
    public void WrongUnitCasing_SuggestionHasLongName(string expression, string symbol, string expectedLongNameFragment) {
        var r = fixture.Engine.Evaluate(expression);
        Assert.NotNull(r.ErrorSuggestions);
        var s = r.ErrorSuggestions.First(x => x.Symbol == symbol);
        Assert.Contains(expectedLongNameFragment, s.LongName, StringComparison.OrdinalIgnoreCase);
    }

    // ── Success ───────────────────────────────────────────────────────────────

    [Fact]
    public void SuccessfulEval_ErrorKindIsNone() {
        var r = fixture.Engine.Evaluate("2 + 2");
        Assert.True(r.IsSuccess);
        Assert.Equal(CalcErrorKind.None, r.ErrorKind);
        Assert.Null(r.ErrorToken);
        Assert.Null(r.ErrorSuggestions);
    }
}
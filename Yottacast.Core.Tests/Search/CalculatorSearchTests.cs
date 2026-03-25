using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search;

[Collection("MathJs")]
public class CalculatorSearchTests(MathJsEngineFixture fixture) {

    private CalculatorSearch BuildSearch(out ClipboardService clipboard) {
        clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        return new CalculatorSearch(fixture.Engine, clipboard);
    }

    private static ResultItemViewModel SearchResult(CalculatorSearch search, string query) {
        var results = search.Search(query, 5);
        return Assert.Single(results);
    }

    // ── Arithmetic detection ──────────────────────────────────────────────────

    public static TheoryData<string, string> ArithmeticCases => new() {
        { "2+2",           "4"    },
        { "10 - 3",        "7"    },
        { "3 * 4",         "12"   },
        { "10 / 4",        "2.5"  },
        { "2^10",          "1024" },
        { "10 % 3",        "1"    },
        { "(2+3) * 4",     "20"   },
        { "100 / (2 + 3)", "20"   },
    };

    [Theory]
    [MemberData(nameof(ArithmeticCases))]
    public void Arithmetic_ReturnsCorrectTitle(string query, string expected) {
        Assert.Equal(expected, SearchResult(BuildSearch(out _), query).Title);
    }

    // ── Math functions ────────────────────────────────────────────────────────

    public static TheoryData<string, string> FunctionCases => new() {
        { "sqrt(144)",  "12"  },
        { "sqrt(2)",    "1.414213562" },
        { "sin(pi/2)",  "1"   },
        { "cos(0)",     "1"   },
        { "abs(-42)",   "42"  },
        { "floor(3.9)", "3"   },
        { "ceil(3.1)",  "4"   },
        { "round(2.6)", "3"   },
        { "log(e)",     "1"   },
        // Case-insensitive function names
        { "Sin(pi/2)",  "1"   },
        { "SQRT(144)",  "12"  },
        { "Cos(0)",     "1"   },
        { "ABS(-42)",   "42"  },
        { "FLOOR(3.9)", "3"   },
        { "CEIL(3.1)",  "4"   },
    };

    [Theory]
    [MemberData(nameof(FunctionCases))]
    public void Functions_ReturnsCorrectTitle(string query, string expected) {
        Assert.Equal(expected, SearchResult(BuildSearch(out _), query).Title);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public void Result_HasCorrectIconCategoryScore() {
        var item = SearchResult(BuildSearch(out _), "2+2");
        Assert.Equal("🧮", item.Icon);
        Assert.Equal("Calculator", item.Category);
        Assert.Equal(4, item.Score);
        Assert.Equal("2+2", item.Subtitle);
    }

    [Fact]
    public void OnActivate_CopiesResultToClipboard() {
        var search = BuildSearch(out var clipboard);
        string copied = "";
        clipboard.Initialize(text => copied = text);

        var item = SearchResult(search, "2+2");
        Assert.NotNull(item.OnActivate);
        item.OnActivate();

        Assert.Equal("4", copied);
    }

    // ── Non-math queries yield nothing ────────────────────────────────────────

    public static TheoryData<string> NonMathCases => new() {
        { "safari"          },   // plain text
        { "hello world"     },   // no digits or operators
        { "2"               },   // digit only, trivially equal → discarded
    };

    [Theory]
    [MemberData(nameof(NonMathCases))]
    public void NonMath_ReturnsEmpty(string query) {
        Assert.Empty(BuildSearch(out _).Search(query, 5));
    }

    // ── Currency conversions ──────────────────────────────────────────────────
    // Rates in StaticCurrencyRateProvider (units per 1 USD): EUR=0.92, JPY=150.5, MXN=17.1, GBP=0.79

    public static TheoryData<string, string> CurrencyConversionCases => new() {
        { "10 USD to EUR",          "9.2 EUR"   },   // basic USD→EUR
        { "100 uSd to EUR",         "92 EUR"    },   // case-insensitive input
        { "10 USD to MXN",          "171 MXN"   },   // USD→MXN  (10 × 17.1)
        { "100 USD to JPY",         "15050 JPY" },   // USD→JPY  (100 × 150.5)
        { "10 USD to GBP",          "7.9 GBP"   },   // USD→GBP  (10 × 0.79)
        { "10 USD",                 "9.2 EUR"   },   // auto-appends "to EUR" (default currency)
        { "10 GBP + 20 EUR to MXN", "588"       },   // cross-currency compound expression
    };

    [Theory]
    [MemberData(nameof(CurrencyConversionCases))]
    public void Currency_Converts(string query, string expectedFragment) {
        Assert.Contains(expectedFragment, SearchResult(BuildSearch(out _), query).Title);
    }

    // ── Unit case-insensitivity ───────────────────────────────────────────────

    public static TheoryData<string, string> UnitCaseCases => new() {
        // NONE-prefix units — siempre seguros
        { "5 MILES to km",             "8.04672 km"         },
        { "10 INCH to cm",             "25.4 cm"            },
        { "5 FOOT to m",               "1.524 m"            },
        { "1 YARD to meter",           "0.9144 meter"       },
        { "32 FAHRENHEIT to celsius",  "0 celsius"          },
        { "100 CELSIUS to fahrenheit", "212 fahrenheit"     },
        // SHORT-prefix units con combinaciones seguras
        { "5 KG to lbs",               "11.02311311 lbs"    },
        { "5 KM to miles",             "3.106855961 miles"  },
        { "100 GRAM to kg",            "0.1 kg"             },
        { "1 RADIAN to degree",        "57.29577951 degree" },
        // Funciones + unidades mezcladas
        { "SQRT(144) km to miles",     "7.456454307 miles"  },
    };

    [Theory, MemberData(nameof(UnitCaseCases))]
    public void Units_CaseInsensitive(string query, string expectedFragment) {
        Assert.Contains(expectedFragment, SearchResult(BuildSearch(out _), query).Title);
    }

    // Verificar que tokens ambiguos (M/m prefix) conservan su casing original
    [Fact]
    public void Units_AmbiguousPrefix_MilliVsMega_NotMutated() {
        // "mg" = miligramo (m+g), "Mg" = megagramo (M+g)
        var r1 = fixture.Engine.Evaluate("1 Mg to g");  // 1 megagramo = 1e6 g
        var r2 = fixture.Engine.Evaluate("1 mg to g");  // 1 miligramo = 0.001 g
        Assert.True(r1.IsSuccess, r1.Error);
        Assert.True(r2.IsSuccess, r2.Error);
        Assert.NotEqual(r1.Value, r2.Value);
        Assert.Contains("1e+6", r1.Value!); // 1 Mg = 1,000,000 g (math.js formato científico)
    }

    // ── Non-currency units do NOT trigger auto-conversion ─────────────────────

    public static TheoryData<string> NonCurrencyUnitCases => new() {
        { "2 kg + 3 g"     },   // mass units only
        { "5 miles to km"  },   // explicit unit conversion, no currency
        { "2 km + 500 m"   },   // distance units
    };

    [Theory]
    [MemberData(nameof(NonCurrencyUnitCases))]
    public void NonCurrencyUnits_DoNotAutoAppendCurrencyConversion(string query) {
        var item = SearchResult(BuildSearch(out _), query);
        Assert.DoesNotContain("EUR", item.Title);
        Assert.DoesNotContain("USD", item.Title);
    }
}

// ── Currency rate update tests ────────────────────────────────────────────────

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
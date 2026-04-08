using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class CalculatorSearchTests(MathJsEngineFixture fixture) {

    private CalculatorSearch BuildSearch(out ClipboardService clipboard) {
        clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        return new CalculatorSearch(fixture.Engine, clipboard);
    }

    private static BaseResultItemViewModel SearchResult(CalculatorSearch search, string query) {
        var results = search.Search(query, 5);
        return Assert.Single(results);
    }

    private static ResultItemViewModel StandardResult(CalculatorSearch search, string query) =>
        Assert.IsType<ResultItemViewModel>(SearchResult(search, query));

    private static string ValueOf(BaseResultItemViewModel item) => item switch {
        ConversionResultItemViewModel c => c.ToShort,
        ResultItemViewModel r => r.Title,
        _ => throw new InvalidOperationException($"Unknown type: {item.GetType()}")
    };

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
        Assert.Equal(expected, StandardResult(BuildSearch(out _), query).Title);
    }

    // ── Math functions ────────────────────────────────────────────────────────

    public static TheoryData<string, string> FunctionCases => new() {
        { "sqrt(144)",  "12"  },
        { "sqrt(2)",    "1.41" },
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
        Assert.Equal(expected, StandardResult(BuildSearch(out _), query).Title);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public void Result_HasCorrectIconCategoryScore() {
        var item = StandardResult(BuildSearch(out _), "2+2");
        Assert.Equal("🧮", item.Icon);
        Assert.Equal("Calculator", item.Category);
        Assert.Equal(4, item.Score);
        Assert.Equal("2 + 2", item.Subtitle);
    }

    [Fact]
    public void OnActivate_CopiesResultToClipboard() {
        var search = BuildSearch(out var clipboard);
        string copied = "";
        clipboard.Initialize(text => copied = text);

        var item = StandardResult(search, "2+2");
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
        Assert.Contains(expectedFragment, ValueOf(SearchResult(BuildSearch(out _), query)));
    }

    // ── Unit case-insensitivity ───────────────────────────────────────────────

    public static TheoryData<string, string> UnitCaseCases => new() {
        // NONE-prefix units — siempre seguros
        { "5 MILES to km",             "8.05 km"            },
        { "10 INCH to cm",             "25.4 cm"            },
        { "5 FOOT to m",               "1.52 m"             },
        { "1 YARD to meter",           "0.914 m"            },
        { "32 FAHRENHEIT to celsius",  "0 °C"               },
        { "100 CELSIUS to fahrenheit", "212 °F"             },
        // SHORT-prefix units con combinaciones seguras
        { "5 KG to lbs",               "11.02 lb"           },
        { "5 KM to miles",             "3.11 mi"            },
        { "100 GRAM to kg",            "0.1 kg"             },
        { "1 RADIAN to degree",        "57.3 deg"           },
        // Funciones + unidades mezcladas
        { "SQRT(144) km to miles",     "7.46 mi"            },
    };

    [Theory, MemberData(nameof(UnitCaseCases))]
    public void Units_CaseInsensitive(string query, string expectedFragment) {
        Assert.Contains(expectedFragment, ValueOf(SearchResult(BuildSearch(out _), query)));
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

    // ── Ambiguity hints ───────────────────────────────────────────────────────

    // Tokens with no ambiguityOverride: expressions use addition so result differs from query.
    // "gt" → ambiguous (Gt=gigatonne vs GT=gigatesla), no override configured.

    public static TheoryData<string, string, string> HintCases => new() {
        { "1 gt + 1 gt", "GT", "gigatesla" },  // gt not canonical, no override → "Maybe you meant GT (gigatesla)?"
        { "1 MG + 1 MG", "Mg", "megagram"  },  // MG not canonical, override fires → "Maybe you meant Mg (megagram)?"
        { "1 mB + 1 mB", "Mb", "Mb"        },  // mB not canonical, override fires → "Maybe you meant Mb (Mb)?"
    };

    [Theory]
    [MemberData(nameof(HintCases))]
    public void AmbiguousUnit_ShowsHintInSubtitle(string query, string sym1, string sym2) {
        var item = StandardResult(BuildSearch(out _), query);
        Assert.Contains("Maybe you meant", item.Subtitle);
        Assert.Contains(sym1, item.Subtitle);
        Assert.Contains(sym2, item.Subtitle);
    }

    public static TheoryData<string> NoHintCases => new() {
        { "2+2"         },  // no units at all
        { "1 mg + 1 mg" },  // mg is exact canonical form → no ambiguity
        { "1 MB + 1 MB" },  // MB is exact canonical form → no ambiguity
    };

    [Theory]
    [MemberData(nameof(NoHintCases))]
    public void UnambiguousQuery_NoHintInSubtitle(string query) {
        var item = StandardResult(BuildSearch(out _), query);
        Assert.DoesNotContain("Maybe you meant", item.Subtitle);
    }

    // ── Error items ───────────────────────────────────────────────────────────

    // Truly unknown unit in a math-like expression → UnknownSymbol hint
    [Fact]
    public void UnknownUnit_InMathContext_ShowsErrorItem() {
        var search = BuildSearch(out _);
        Assert.Empty(search.Search("1 XYZUNIT to g", 5));
        Assert.Null(search.LastHint);
    }

    // Incompatible units (mass vs length) → IncompatibleUnits hint
    [Fact]
    public void IncompatibleUnits_ShowsErrorItem() {
        var search = BuildSearch(out _);
        Assert.Empty(search.Search("1 kg to meter", 5));
        Assert.NotNull(search.LastHint);
    }

    // Plain text without digits or operators → no error item shown
    [Fact]
    public void NonMathQuery_StillReturnsEmpty_OnError() {
        Assert.Empty(BuildSearch(out _).Search("safari", 5));
        Assert.Empty(BuildSearch(out _).Search("hello world", 5));
    }

    // ── Auto-conversión de moneda por defecto ──────────────────────────────────

    public static TheoryData<string> SingleCurrencyUnitCases => new() {
        { "10 USD"  },   // → auto-añade to EUR
        { "50 GBP"  },   // → auto-añade to EUR
        { "100 MXN" },   // → auto-añade to EUR
    };

    [Theory]
    [MemberData(nameof(SingleCurrencyUnitCases))]
    public void AutoConversion_SingleCurrencyUnit_AddsDefaultCurrency(string query) {
        Assert.Contains("EUR", ValueOf(SearchResult(BuildSearch(out _), query)));
    }

    public static TheoryData<string> SumOrArithmeticCurrencyCases => new() {
        { "10 USD + 5 MXN"       },   // raíz es +
        { "(10 USD + 5 MXN) / 2" },   // raíz es /
        { "10 USD * 2"           },   // raíz es *
    };

    [Theory]
    [MemberData(nameof(SumOrArithmeticCurrencyCases))]
    public void AutoConversion_SumOrArithmetic_DoesNotAddDefaultCurrency(string query) {
        var item = SearchResult(BuildSearch(out _), query);
        Assert.DoesNotContain("EUR", ValueOf(item));
    }

    public static TheoryData<string, string> ExplicitConversionCases => new() {
        { "10 USD to MXN",   "MXN" },
        { "100 MXN to GBP",  "GBP" },
    };

    [Theory]
    [MemberData(nameof(ExplicitConversionCases))]
    public void AutoConversion_ExplicitToConversion_DoesNotAddDefaultCurrency(string query, string expectedCurrency) {
        var value = ValueOf(SearchResult(BuildSearch(out _), query));
        Assert.Contains(expectedCurrency, value);
        Assert.DoesNotContain("EUR", value);
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
        var value = ValueOf(SearchResult(BuildSearch(out _), query));
        Assert.DoesNotContain("EUR", value);
        Assert.DoesNotContain("USD", value);
    }

    // ── Auto-conversión de unidades físicas ────────────────────────────────────

    public static TheoryData<string, string> PhysicalAutoConversionCases => new() {
        { "10 kg",         "lb"   },   // mass: kg → lb
        { "5 km",          "mile" },   // length: km → mile
        { "10 centimeter", "in"   },   // long-form → same target as "cm"
        { "5 kilometer",   "mile" },   // long-form → same target as "km"
        { "3 kilogram",    "lb"   },   // long-form → same target as "kg"
    };

    [Theory]
    [MemberData(nameof(PhysicalAutoConversionCases))]
    public void AutoConversion_SinglePhysicalUnit_AddsDefaultTarget(string query, string expectedUnitFragment) {
        Assert.Contains(expectedUnitFragment, ValueOf(SearchResult(BuildSearch(out _), query)));
    }

    // ── ConversionResultItemViewModel ─────────────────────────────────────────

    [Fact]
    public void Conversion_ReturnsConversionResultItemViewModel() {
        var item = SearchResult(BuildSearch(out _), "10 km to m");
        Assert.IsType<ConversionResultItemViewModel>(item);
    }

    public static TheoryData<string, string, string> ConversionLongFormCases => new() {
        { "10 km to m",   "10000 meters",    "10 kilometers"  },
        { "1000 m to km", "1 kilometer",     "1000 meters"    },   // singular on destination (toValue == 1)
        { "5 kg to g",    "5000 grams",      "5 kilograms"    },
    };

    [Theory, MemberData(nameof(ConversionLongFormCases))]
    public void Conversion_LongFormFields(string query, string expectedToLong, string expectedFromLong) {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), query));
        Assert.Contains(expectedToLong,   item.ToLong   ?? "");
        Assert.Contains(expectedFromLong, item.FromLong ?? "");
    }

    [Fact]
    public void Conversion_Currency_NoLongForm() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 USD to EUR"));
        Assert.Null(item.ToLong);
        Assert.Null(item.FromLong);
    }

    // ── Ambiguity hints en ConversionResultItemViewModel ──────────────────────

    // "10 MG" → ambiguityOverrides resuelve a mg (milligram), hint con alternativa Mg (megagram)
    [Fact]
    public void AmbiguousUnit_WithOverride_ResolvesToExpectedUnit_WithAlternativeHint() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 MG"));
        Assert.Contains("mg", item.FromShort);     // resuelto a milligram, no megagram
        Assert.NotNull(item.AmbiguityHint);        // hint con alternativa
        Assert.Contains("Mg", item.AmbiguityHint); // "Maybe you meant Mg (megagram)?"
    }

    // "10 gt" → sin override: Gt (gigatonne) con hint en formato "Maybe you meant GT (gigatesla)?"
    // Gt no está en defaultTargets — necesita el par dimensional ["kg","lb"] en defaultPairs para
    // producir un ConversionResultItemViewModel en lugar de un ResultItemViewModel de calculadora.
    [Fact]
    public void AmbiguousUnit_WithoutOverride_ShowsMaybeYouMeantHint() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 gt"));
        Assert.NotNull(item.AmbiguityHint);
        Assert.Contains("Maybe you meant", item.AmbiguityHint);
        Assert.Contains("GT", item.AmbiguityHint);
        Assert.Contains("gigatesla", item.AmbiguityHint);
    }

    // "10 kg" → unidad canónica, sin hint de ambigüedad
    [Fact]
    public void UnambiguousUnit_InConversion_NoAmbiguityHint() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 kg"));
        Assert.Null(item.AmbiguityHint);
    }

    // ── defaultPairs: fallback dimensional para unidades exóticas no en defaultTargets ──────────
    // Las unidades comunes (m, ft, kg, lb…) tienen entradas directas en defaultTargets.
    // defaultPairs cubre el resto por matching dimensional: cualquier unidad con la misma
    // dimensión física que el primer elemento del par recibe el target del par.
    // Si se eliminan estos pares de defaultPairs, las unidades de abajo pasan a kind=calculation
    // y el resultado es ResultItemViewModel en lugar de ConversionResultItemViewModel.

    // Longitud: Mm (megámetro) no está en defaultTargets → target "m" vía par ["m","ft"]
    // Para unidades exóticas, findDefaultTarget devuelve pair[0] (la base SI del par), no pair[1].
    [Fact]
    public void DefaultPairs_MegameterUsesLengthDimensionalFallback() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 Mm"));
        Assert.Contains("m", item.ToShort);   // target = "m" (pair[0]), no "ft"
    }

    // Masa: Gg (gigagramo) no está en defaultTargets → target "kg" vía par ["kg","lb"]
    [Fact]
    public void DefaultPairs_GigagramUsesMassDimensionalFallback() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 Gg"));
        Assert.Contains("kg", item.ToShort);  // target = "kg" (pair[0]), no "lb"
    }

    // Masa: Gt (gigatonne) no está en defaultTargets → target "kg" vía par ["kg","lb"]
    // (mismo mecanismo que Gg; verifica que la cobertura dimensional incluye alias de masa como tonne)
    [Fact]
    public void DefaultPairs_GigatonneUsesMassDimensionalFallback() {
        var item = Assert.IsType<ConversionResultItemViewModel>(SearchResult(BuildSearch(out _), "10 Gt"));
        Assert.Contains("kg", item.ToShort);  // target = "kg" (pair[0]), no "lb"
    }
}


using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Search;

[Collection("MathJs")]
public class CalculatorSearchTests(MathJsEngineFixture fixture) {

    private CalculatorSearch BuildSearch(out ClipboardService clipboard) {
        clipboard = new ClipboardService();
        return new CalculatorSearch(fixture.Engine, clipboard);
    }

    private static async Task<IReadOnlyList<Yottacast.Core.ViewModels.ResultItemViewModel>> SearchAsync(
        CalculatorSearch search, string query) {
        await foreach (var snapshot in search.SearchAsync(query, 5))
            return snapshot;
        return [];
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
    public async Task Arithmetic_ReturnsCorrectTitle(string query, string expected) {
        var results = await SearchAsync(BuildSearch(out _), query);
        Assert.Single(results);
        Assert.Equal(expected, results[0].Title);
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
    };

    [Theory]
    [MemberData(nameof(FunctionCases))]
    public async Task Functions_ReturnsCorrectTitle(string query, string expected) {
        var results = await SearchAsync(BuildSearch(out _), query);
        Assert.Single(results);
        Assert.Equal(expected, results[0].Title);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_HasCorrectIconCategoryScore() {
        var results = await SearchAsync(BuildSearch(out _), "2+2");
        var item = Assert.Single(results);
        Assert.Equal("🧮", item.Icon);
        Assert.Equal("Calculator", item.Category);
        Assert.Equal(4, item.Score);
        Assert.Equal("2+2", item.Subtitle);
    }

    [Fact]
    public async Task OnActivate_CopiesResultToClipboard() {
        var search = BuildSearch(out var clipboard);
        string copied = "";
        clipboard.Initialize(text => copied = text);

        var results = await SearchAsync(search, "2+2");
        var item = Assert.Single(results);
        Assert.NotNull(item.OnActivate);
        item.OnActivate();

        Assert.Equal("4", copied);
    }

    // ── Non-math queries yield nothing ────────────────────────────────────────

    public static TheoryData<string> NonMathCases => new() {
        { "safari"          },   // plain text
        { "hello world"     },   // no digits or operators
        { "2"               },   // digit only, trivially equal → discarded
        { "100 usd to eur"  },   // currency — math.js has no fx rates
    };

    [Theory]
    [MemberData(nameof(NonMathCases))]
    public async Task NonMath_ReturnsEmpty(string query) {
        var results = await SearchAsync(BuildSearch(out _), query);
        Assert.Empty(results);
    }
}

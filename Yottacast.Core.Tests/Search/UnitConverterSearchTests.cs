using Xunit;
using Yottacast.Core.Search;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Search;

[Collection("MathJs")]
public class UnitConverterSearchTests(MathJsEngineFixture fixture) {

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

    // ── Conversions ───────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> ConversionCases => new() {
        { "10 kg to lbs",                "22.04622622 lbs",   "lbs"     },
        { "1 kg to g",                   "1000 g",            "g"       },
        { "5 miles to km",               "8.04672 km",        "km"      },
        { "1 km to m",                   "1000 m",            "m"       },
        { "100 fahrenheit to celsius",   "37.77777778 celsius", "celsius" },
        { "0 celsius to fahrenheit",     "32 fahrenheit",     "fahrenheit" },
        { "1 hour to seconds",           "3600 seconds",      "seconds" },
        { "1 day to hours",              "24 hours",          "hours"   },
        { "1 litre to ml",               "1000 ml",           "ml"      },
    };

    [Theory]
    [MemberData(nameof(ConversionCases))]
    public async Task Conversion_TitleMatchesExpected(string query, string expectedTitle, string unitHint) {
        var results = await SearchAsync(BuildSearch(out _), query);
        var item = Assert.Single(results);
        Assert.Equal(expectedTitle, item.Title);
        _ = unitHint; // used as documentation only
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Result_HasCorrectIconCategoryScore() {
        var results = await SearchAsync(BuildSearch(out _), "10 kg to lbs");
        var item = Assert.Single(results);
        Assert.Equal("📐", item.Icon);
        Assert.Equal("Converter", item.Category);
        Assert.Equal(4, item.Score);
        Assert.Equal("10 kg to lbs", item.Subtitle);
    }

    [Fact]
    public async Task OnActivate_CopiesResultToClipboard() {
        var search = BuildSearch(out var clipboard);
        string copied = "";
        clipboard.Initialize(text => copied = text);

        var results = await SearchAsync(search, "1 km to m");
        var item = Assert.Single(results);
        Assert.NotNull(item.OnActivate);
        item.OnActivate();

        Assert.Equal("1000 m", copied);
    }

    // ── Non-conversion queries yield nothing ──────────────────────────────────

    public static TheoryData<string> NonConversionCases => new() {
        { "10"          },   // number only — trivially equal, discarded
    };

    [Theory]
    [MemberData(nameof(NonConversionCases))]
    public async Task NonConversion_ReturnsEmpty(string query) {
        var results = await SearchAsync(BuildSearch(out _), query);
        Assert.Empty(results);
    }
}

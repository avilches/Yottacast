using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class UnitConverterSearchTests(MathJsEngineFixture fixture) {

    private CalculatorSearch BuildSearch(out ClipboardService clipboard) {
        clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        return new CalculatorSearch(fixture.Engine, clipboard);
    }

    private static IReadOnlyList<ViewModels.ConversionResultItemViewModel> SearchResults(
        CalculatorSearch search, string query) {
        return search.Search(query, 5)
            .Cast<ViewModels.ConversionResultItemViewModel>().ToList();
    }

    // ── Conversions ───────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> ConversionCases => new() {
        { "10 kg to lbs",                "22.04622622 lb",    "lb"      },
        { "1 kg to g",                   "1000 g",            "g"       },
        { "5 miles to km",               "8.04672 km",        "km"      },
        { "1 km to m",                   "1000 m",            "m"       },
        // Temperatura: long-form aliases normalize to canonical (degC/degF) with display names °C/°F
        { "100 fahrenheit to celsius",   "37.77777778 °C",    "°C"      },
        { "0 celsius to fahrenheit",     "32 °F",             "°F"      },
        // Tiempo: long-form "hour"→"h", plural "seconds"→"s" normalize before evaluation
        { "1 hour to seconds",           "3600 s",            "s"       },
        { "1 day to hours",              "24 h",              "h"       },
        { "1 litre to ml",               "1000 ml",           "ml"      },
        // Case-insensitive: operator TO y unidades en mayúscula
        { "10 km TO miles",              "6.213711922 mi",    "mi"      },
        { "10 KG to lbs",                "22.04622622 lb",    "lb"      },
        { "100 FAHRENHEIT to celsius",   "37.77777778 °C",    "°C"      },
        { "1 HOUR to seconds",           "3600 s",            "s"       },
    };

    [Theory]
    [MemberData(nameof(ConversionCases))]
    public void Conversion_TitleMatchesExpected(string query, string expectedTitle, string unitHint) {
        var results = SearchResults(BuildSearch(out _), query);
        var item = Assert.Single(results);
        Assert.Equal(expectedTitle, item.ToShort);
        _ = unitHint; // used as documentation only
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public void Result_HasCorrectIconCategoryScore() {
        var results = SearchResults(BuildSearch(out _), "10 kg to lbs");
        var item = Assert.Single(results);
        Assert.Equal("📐", item.Icon);
        Assert.Equal("Converter", item.Category);
        Assert.Equal(4, item.Score);
    }

    [Fact]
    public void OnActivate_CopiesResultToClipboard() {
        var search = BuildSearch(out var clipboard);
        string copied = "";
        clipboard.Initialize(text => copied = text);

        var results = SearchResults(search, "1 km to m");
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
    public void NonConversion_ReturnsEmpty(string query) {
        var results = SearchResults(BuildSearch(out _), query);
        Assert.Empty(results);
    }
}

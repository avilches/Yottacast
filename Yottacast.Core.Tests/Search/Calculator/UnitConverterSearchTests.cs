using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class UnitConverterSearchTests(MathJsEngineFixture fixture) {

    private (CalculatorSearch Search, Func<string?> GetLastCopied) CreateSearch() {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        string? lastCopied = null;
        clipboard.Initialize(copy: text => lastCopied = text, read: () => Task.FromResult<string?>(null));
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var provider = MathJsEngineProvider.ForTesting(fixture.Engine);
        var exchangeRateService = new ExchangeRateService(new HttpClient(), settings, NullLogger<ExchangeRateService>.Instance);
        var search = new CalculatorSearch(provider, exchangeRateService, clipboard, settings, NullLogger<CalculatorSearch>.Instance);
        return (search, () => lastCopied);
    }

    private CalculatorSearch BuildSearch(out Func<string?> getLastCopied) {
        var (search, get) = CreateSearch();
        getLastCopied = get;
        return search;
    }

    private static IReadOnlyList<ViewModels.ConversionResultItemViewModel> SearchResults(
        CalculatorSearch search, string query) {
        return search.Search(query, 5)
            .Cast<ViewModels.ConversionResultItemViewModel>().ToList();
    }

    // ── Conversions ───────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> ConversionCases => new() {
        { "10 kg to lbs",                "22.05 lb",          "lb"      },
        { "1 kg to g",                   "1000 g",            "g"       },
        { "5 miles to km",               "8.05 km",           "km"      },
        { "1 km to m",                   "1000 m",            "m"       },
        // Temperatura: long-form aliases normalize to canonical (degC/degF) with display names °C/°F
        { "100 fahrenheit to celsius",   "37.78 °C",          "°C"      },
        { "0 celsius to fahrenheit",     "32 °F",             "°F"      },
        // Tiempo: long-form "hour"→"h", plural "seconds"→"s" normalize before evaluation
        { "1 hour to seconds",           "3600 s",            "s"       },
        { "1 day to hours",              "24 h",              "h"       },
        { "1 litre to ml",               "1000 ml",           "ml"      },
        // Case-insensitive: operator TO y unidades en mayúscula
        { "10 km TO miles",              "6.21 mi",           "mi"      },
        { "10 KG to lbs",                "22.05 lb",          "lb"      },
        { "100 FAHRENHEIT to celsius",   "37.78 °C",          "°C"      },
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
        Assert.Equal(7, item.Score);
    }

    [Fact]
    public void OnActivate_CopiesResultToClipboard() {
        var (search, getLastCopied) = CreateSearch();

        var results = SearchResults(search, "1 km to m");
        var item = Assert.Single(results);
        item.Actions.Single(a => a.Hotkey == ActionHotkey.Enter).Execute();

        Assert.Equal("1000 m", getLastCopied());
    }

    [Fact]
    public void ConversionResult_HasCopyActions() {
        var (search, _) = CreateSearch();
        var results = search.Search("5 km to miles", 5);
        var item = Assert.Single(results.OfType<ViewModels.ConversionResultItemViewModel>());

        var enterAction = item.Actions.Single(a => a.Hotkey == ActionHotkey.Enter);
        Assert.True(enterAction.PasteAfterClose);

        Assert.NotNull(item.Actions.FirstOrDefault(a => a.Hotkey == ActionHotkey.MetaC));
        Assert.Equal("Result copied!", item.Actions.Single(a => a.Hotkey == ActionHotkey.MetaC).HintProvider?.Invoke());
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

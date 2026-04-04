using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

[Collection("MathJs")]
public class DefaultConversionTests(MathJsEngineFixture fixture) {

    private ConversionResultItemViewModel GetConversionItem(string query) {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var search = new CalculatorSearch(fixture.Engine, clipboard);
        var results = search.Search(query, 5);
        var item = Assert.Single(results);
        return Assert.IsType<ConversionResultItemViewModel>(item);
    }

    // Formats both short and long forms: "10 km / 10 kilometers" or just "10 B" when long is null.
    private static string Fmt(string s, string? l) => l is null ? s : $"{s} / {l}";

    // ── Casos de conversión por defecto ──────────────────────────────────────

    public static TheoryData<string, string> DefaultConversionCases => new() {
        // Temperatura: aliases c/f vs. C/F mayúscula
        { "10c",      "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        // 10C is coulomb
        { "10ºc",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10ºC",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10f",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        // 10F is Faraday
        { "10ºf",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        { "10ºF",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        { "10 degc",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 DEGC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degf",  "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        { "10 DEGF",  "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        // Unidades eléctricas/físicas mayúscula
        // 10c is celsius
        { "10C",      "10 C / 10 coulombs -> 10000 mC / 10000 millicoulombs"                      },
        // 10f is fahrenheit
        { "10F",      "10 F / 10 farads -> 1e+7 uF / 1e+7 microfarads"                            },
        { "10v",      "10 V / 10 volts -> 10000 mV / 10000 millivolts"                            },
        { "10V",      "10 V / 10 volts -> 10000 mV / 10000 millivolts"                            },
        { "10a",      "10 A / 10 amperes -> 10000 mA / 10000 milliamperes"                        },
        { "10A",      "10 A / 10 amperes -> 10000 mA / 10000 milliamperes"                        },
        { "10w",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10W",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        // 10h is hour
        { "10H",      "10 H / 10 henrys -> 10000 mH / 10000 millihenrys"                          },
        // 10 t is tonne
        { "10T",      "10 T / 10 teslas -> 10000 mT / 10000 milliteslas"                          },
        // Tiempo: h ≠ H, a ≠ A
        { "10h",      "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hour",   "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hours",   "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10 d",     "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 day",   "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 days",  "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 min",   "10 min / 10 minutes -> 600 s / 600 seconds"                                },
        { "10s",      "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10ms",     "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10Ms",     "10 Ms / 10 megaseconds -> 2777.777778 h / 2777.777778 hours"               },
        // Masa
        { "10t",      "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"                            },
        { "10 g",     "10 g / 10 grams -> 0.3527396195 oz"                                        },
        // Otros SI / métrico↔imperial
        { "10 B",     "10 B -> 0.01 kB"                                                           },
        { "10 J",     "10 J / 10 joules -> 0.009478171203 BTU"                                    },
        { "10 N",     "10 N / 10 newtons -> 2.248089431 lbf"                                      },
        { "10 Pa",    "10 Pa -> 0.001450377377 psi"                                               },
        { "10 rad",   "10 rad / 10 radians -> 572.9577951 deg / 572.9577951 degrees"              },
    };

    [Theory]
    [MemberData(nameof(DefaultConversionCases))]
    public void DefaultConversion_Summary(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    // ── Nombres largos ───────────────────────────────────────────────────────

    [Fact]
    public void DefaultConversion_LongNames_Celsius() {
        var item = GetConversionItem("10c");
        Assert.NotNull(item.FromLong);
        Assert.NotNull(item.ToLong);
        Assert.Contains("celsius", item.FromLong, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fahrenheit", item.ToLong, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultConversion_LongNames_Volt() {
        var item = GetConversionItem("10V");
        Assert.NotNull(item.FromLong);
        Assert.NotNull(item.ToLong);
        Assert.Contains("volt", item.FromLong, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("millivolt", item.ToLong, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultConversion_LongNames_Hour() {
        var item = GetConversionItem("10h");
        Assert.Equal("10 h", item.FromShort);
        Assert.Equal("600 min", item.ToShort);
        // Nombres largos vienen de longNames en unit-config.json
        Assert.NotNull(item.FromLong);
        Assert.Contains("hour", item.FromLong, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(item.ToLong);
        Assert.Contains("minute", item.ToLong, StringComparison.OrdinalIgnoreCase);
    }

    // ── Formas largas y plurales de unidades de tiempo/temperatura ──────────
    // Cualquier sinónimo (10h, 10hour, 10 hours, 10 Hours, 10 HOURS) debe
    // normalizarse internamente a la unidad canónica y producir el mismo resultado.

    public static TheoryData<string, string> UnitAliasCases => new() {
        // Tiempo — formas largas (sinónimos singulares, cubiertos por auto-reverse de longNames)
        { "10 hour",        "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 second",      "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"        },
        { "10 millisecond", "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"          },
        // Tiempo — plurales (cubiertos por tokenAliases)
        { "10 hours",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 seconds",     "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"        },
        { "10 milliseconds","10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"          },
        { "10 minutes",     "10 min / 10 minutes -> 600 s / 600 seconds"                },
        { "10 days",        "10 day / 10 days -> 240 h / 240 hours"                     },
        { "10 weeks",       "10 week / 10 weeks -> 70 day / 70 days"                    },
        { "10 months",      "10 month / 10 months -> 304.375 day / 304.375 days"        },
        { "10 years",       "10 year -> 3652.5 day / 3652.5 days"                       },
        // Tiempo — formas largas con capitalización variada (multi-char override case-insensitive)
        { "10 Hour",        "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 Hours",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 HOURS",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        // Temperatura — formas largas (cubiertos por auto-reverse de longNames)
        { "100 celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 fahrenheit", "100 °F / 100 fahrenheit -> 37.77777778 °C / 37.77777778 celsius" },
        // Temperatura — capitalización variada
        { "100 Celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 FAHRENHEIT", "100 °F / 100 fahrenheit -> 37.77777778 °C / 37.77777778 celsius" },
    };

    [Theory]
    [MemberData(nameof(UnitAliasCases))]
    public void UnitAlias_NormalizesToCanonical(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    [Fact]
    public void LongFormHour_HasCorrectAllFourFields() {
        // "10 hour" debe producir exactamente los mismos 4 campos que "10h"
        var item = GetConversionItem("10 hour");
        Assert.Equal("10 h",    item.FromShort);
        Assert.Equal("600 min", item.ToShort);
        Assert.NotNull(item.FromLong);
        Assert.Contains("hour",   item.FromLong, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(item.ToLong);
        Assert.Contains("minute", item.ToLong, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LongFormSecond_HasCorrectAllFourFields() {
        var item = GetConversionItem("10 second");
        Assert.Equal("10 s",       item.FromShort);
        Assert.Equal("10000 ms",   item.ToShort);
        Assert.NotNull(item.FromLong);
        Assert.Contains("second",      item.FromLong, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(item.ToLong);
        Assert.Contains("millisecond", item.ToLong, StringComparison.OrdinalIgnoreCase);
    }
}

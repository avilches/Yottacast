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

    // ── Casos de conversión por defecto ──────────────────────────────────────

    public static TheoryData<string, string> DefaultConversionCases => new() {
        // Temperatura: aliases c/f vs. C/F mayúscula
        { "10c",      "10 °C / 50 °F"                   },
        { "10f",      "10 °F / -12.22222222 °C"          },
        { "10 degC",  "10 °C / 50 °F"                    },
        { "10 degF",  "10 °F / -12.22222222 °C"          },
        // Unidades eléctricas/físicas mayúscula
        { "10C",      "10 C / 10000 mC"                  },
        { "10 F",     "10 F / 1e+7 uF"                   },
        { "10V",      "10 V / 10000 mV"                  },
        { "10v",      "10 V / 10000 mV"                  },
        { "10A",      "10 A / 10000 mA"                  },
        { "10W",      "10 W / 0.01 kW"                   },
        { "10w",      "10 W / 0.01 kW"                   },
        { "10H",      "10 H / 10000 mH"                  },
        { "10T",      "10 T / 10000 mT"                  },
        // Tiempo: h ≠ H, a ≠ A
        { "10h",      "10 h / 600 min"                   },
        { "10 day",   "10 day / 240 h"                   },
        { "10 min",   "10 min / 600 s"                   },
        { "10s",      "10 s / 10000 ms"                  },
        // "10a" no se incluye: en este math.js "a" no es año (no está en UNITS),
        //   se resuelve como "A" (amperio) → "10 A / 10000 mA"
        // Masa
        { "10t",      "10 t / 10000 kg"                    },
        { "10 g",     "10 g / 0.3527396195 oz"            },
        // Otros SI / métrico↔imperial
        { "10 B",     "10 B / 0.01 kB"                    },
        { "10 J",     "10 J / 0.009478171203 BTU"         },
        { "10 N",     "10 N / 2.248089431 lbf"            },
        { "10 Pa",    "10 Pa / 0.001450377377 psi"        },
        { "10 rad",   "10 rad / 572.9577951 deg"          },
    };

    [Theory]
    [MemberData(nameof(DefaultConversionCases))]
    public void DefaultConversion_Summary(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{item.FromShort} / {item.ToShort}";
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
}

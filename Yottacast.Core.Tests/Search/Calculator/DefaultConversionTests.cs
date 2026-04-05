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
        // ── Temperatura ──────────────────────────────────────────────────────
        // aliases c/f vs. C/F mayúscula
        { "10c",      "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        // 10C is coulomb
        { "10ºc",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10ºC",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10f",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        // 10F is Faraday
        { "10ºf",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                      },
        { "10ºF",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                      },
        { "10 degc",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 DEGC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degf",  "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        { "10 DEGF",  "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        // ── Electricidad/magnetismo ──────────────────────────────────────────
        // 10c is celsius
        { "10C",      "10 C / 10 coulombs -> 10000 mC / 10000 millicoulombs"                      },
        // 10f is fahrenheit
        { "10F",      "10 F / 10 farads -> 1e+7 uF / 1e+7 microfarads"                            },
        { "10v",      "10 V / 10 volts -> 10000 mV / 10000 millivolts"                            },
        { "10V",      "10 V / 10 volts -> 10000 mV / 10000 millivolts"                            },
        { "10Volts",  "10 V / 10 volts -> 10000 mV / 10000 millivolts"                            },
        { "10a",      "10 A / 10 amperes -> 10000 mA / 10000 milliamperes"                        },
        { "10A",      "10 A / 10 amperes -> 10000 mA / 10000 milliamperes"                        },
        { "10amPeres","10 A / 10 amperes -> 10000 mA / 10000 milliamperes"                        },
        { "10w",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10W",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10watts",  "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        // 10h is hour
        { "10H",      "10 H / 10 henrys -> 10000 mH / 10000 millihenrys"                          },
        { "10Henrys", "10 H / 10 henrys -> 10000 mH / 10000 millihenrys"                          },
        // 10 t is tonne
        { "10T",      "10 T / 10 teslas -> 10000 mT / 10000 milliteslas"                          },
        { "10Teslas", "10 T / 10 teslas -> 10000 mT / 10000 milliteslas"                          },
        // ── Tiempo ──────────────────────────────────────────────────────────
        // h ≠ H, a ≠ A
        { "10h",           "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hour",        "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hours",       "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10 d",          "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 day",        "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 days",       "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 min",        "10 min / 10 minutes -> 600 s / 600 seconds"                                },
        { "10s",           "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10second",      "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10seconds",     "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10ms",          "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10millisecond", "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10milliseconds","10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10Ms",          "10 Ms / 10 megaseconds -> 2777.78 h / 2777.78 hours"                      },
        { "10megasecond",  "10 Ms / 10 megaseconds -> 2777.78 h / 2777.78 hours"                      },
        { "10megaseconds", "10 Ms / 10 megaseconds -> 2777.78 h / 2777.78 hours"                      },
        // ── Masa ────────────────────────────────────────────────────────────
        { "10t",      "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"                            },
        { "10tonnes", "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"                            },
        { "10 g",     "10 g / 10 grams -> 0.3527396195 oz / 0.3527396195 ounces"                 },
        { "10 grams", "10 g / 10 grams -> 0.3527396195 oz / 0.3527396195 ounces"                 },
        { "10 oz",    "10 oz / 10 ounces -> 283.5 g / 283.5 grams"                               },
        { "10 ounces","10 oz / 10 ounces -> 283.5 g / 283.5 grams"                               },
        { "10 lb",    "10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"                            },
        { "10 lbs",   "10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"                            },
        { "10 pound", "10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"                            },
        { "10 pounds","10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"                            },
        // ── Longitud ────────────────────────────────────────────────────────
        { "10 m",           "10 m / 10 meters -> 32.81 ft / 32.81 feet"                               },
        { "10 meter",       "10 m / 10 meters -> 32.81 ft / 32.81 feet"                               },
        { "10 meters",      "10 m / 10 meters -> 32.81 ft / 32.81 feet"                               },
        { "10 km",          "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"                          },
        { "10 kilometer",   "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"                          },
        { "10 kilometers",  "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"                          },
        { "10 cm",          "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"                          },
        { "10 centimeter",  "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"                          },
        { "10 centimeters", "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"                          },
        { "10 mm",          "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 ft",          "10 ft / 10 feet -> 3.05 m / 3.05 meters"                                  },
        { "10 feet",        "10 ft / 10 feet -> 3.05 m / 3.05 meters"                                  },
        { "1 foot",         "1 ft / 1 foot -> 0.3048 m / 0.3048 meters"                                },
        { "10 in",          "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 inch",        "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 inches",      "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 yard",        "10 yard / 10 yards -> 9.14 m / 9.14 meters"                               },
        { "10 yards",       "10 yard / 10 yards -> 9.14 m / 9.14 meters"                               },
        { "10 mi",          "10 mi / 10 miles -> 16.09 km / 16.09 kilometers"                          },
        { "10 mile",        "10 mi / 10 miles -> 16.09 km / 16.09 kilometers"                          },
        { "10 miles",       "10 mi / 10 miles -> 16.09 km / 16.09 kilometers"                          },
        // ── Volumen ─────────────────────────────────────────────────────────
        { "10 l",     "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"                           },
        { "10 L",     "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"                           },
        { "10 gal",   "10 gallon / 10 gallons -> 37.85 L / 37.85 litres"                         },
        { "10 gallon","10 gallon / 10 gallons -> 37.85 L / 37.85 litres"                         },
        { "10 gallons","10 gallon / 10 gallons -> 37.85 L / 37.85 litres"                        },
        // ── Presión ─────────────────────────────────────────────────────────
        { "10 Pa",         "10 Pa / 10 pascals -> 0.001450377377 psi"                              },
        { "10 pascals",    "10 Pa / 10 pascals -> 0.001450377377 psi"                              },
        { "10 bar",        "10 bar / 10 bars -> 145.04 psi"                                         },
        { "10 atm",        "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 atmosphere", "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 atmospheres","10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 psi",   "10 psi -> 0.6894757293 bar / 0.6894757293 bars"                           },
        { "10 torr",  "10 torr -> 10 mmHg"                                                       },
        { "10 mmHg",  "10 mmHg -> 1.33 kPa"                                                      },
        // ── Fuerza ──────────────────────────────────────────────────────────
        { "10 N",       "10 N / 10 newtons -> 2.25 lbf / 2.25 pound-forces"                     },
        { "10 newton",  "10 N / 10 newtons -> 2.25 lbf / 2.25 pound-forces"                     },
        { "10 newtons", "10 N / 10 newtons -> 2.25 lbf / 2.25 pound-forces"                     },
        { "10 lbf",   "10 lbf / 10 pound-forces -> 44.48 N / 44.48 newtons"                     },
        { "10 kgf",   "10 kgf / 10 kilogram-forces -> 98.07 N / 98.07 newtons"                  },
        { "10 dyn",   "10 dyn / 10 dynes -> 0.1 mN / 0.1 millinewtons"                          },
        // ── Energía ─────────────────────────────────────────────────────────
        { "10 J",     "10 J / 10 joules -> 0.01 kJ / 0.01 kilojoules"                              },
        { "10 kJ",    "10 kJ / 10 kilojoules -> 2.78 Wh"                                        },
        { "10 Wh",    "10 Wh -> 36 kJ / 36 kilojoules"                                           },
        { "10 eV",    "10 eV / 10 electronvolts -> 1.602176565e-18 J / 1.602176565e-18 joules"  },
        { "10 erg",   "10 erg -> 1e-6 J / 1e-6 joules"                                           },
        // ── Potencia ────────────────────────────────────────────────────────
        { "10 hp",          "10 hp / 10 horsepowers -> 7.46 kW / 7.46 kilowatts"                      },
        { "10 horsepower",  "10 hp / 10 horsepowers -> 7.46 kW / 7.46 kilowatts"                      },
        { "10 horsepowers", "10 hp / 10 horsepowers -> 7.46 kW / 7.46 kilowatts"                      },
        // ── Datos ───────────────────────────────────────────────────────────
        { "10 B",     "10 B / 10 bytes -> 0.01 kB / 0.01 kilobytes"                              },
        { "10 kB",    "10 kB / 10 kilobytes -> 0.01 MB / 0.01 megabytes"                         },
        { "10 MB",    "10 MB / 10 megabytes -> 0.01 GB / 0.01 gigabytes"                         },
        { "10 GB",    "10 GB / 10 gigabytes -> 0.01 TB / 0.01 terabytes"                         },
        { "10 TB",    "10 TB / 10 terabytes -> 10000 GB / 10000 gigabytes"                       },
        // ── Electromagnetismo adicional ─────────────────────────────────────
        { "10 ohm",   "10 ohm / 10 ohms -> 0.01 kohm / 0.01 kilohms"                           },
        { "10 mol",   "10 mol / 10 moles -> 10000 mmol / 10000 millimoles"                       },
        { "10 S",     "10 S / 10 siemens -> 10000 mS / 10000 millisiemens"                       },
        // ── Tiempo adicional ────────────────────────────────────────────────
        { "10 year",   "10 year / 10 years -> 3652.5 day / 3652.5 days"                          },
        { "10 decade", "10 decade / 10 decades -> 100 year / 100 years"                          },
        // ── Volumen menor ───────────────────────────────────────────────────
        { "10 pint",  "10 pint / 10 pints -> 20 cup / 20 cups"                                   },
        { "10 quart", "10 quart / 10 quarts -> 20 pint / 20 pints"                               },
        { "10 cup",   "10 cup / 10 cups -> 80 floz / 80 fluid ounces"                            },
        { "10 floz",  "10 floz / 10 fluid ounces -> 295.74 mL / 295.74 millilitres"              },
        { "10 tbsp",  "10 tablespoon / 10 tablespoons -> 30 teaspoon / 30 teaspoons"             },
        { "10 tsp",   "10 teaspoon / 10 teaspoons -> 50 mL / 50 millilitres"                    },
        { "10 cc",    "10 cc / 10 cubic centimeters -> 10 mL / 10 millilitres"                   },
        // ── Ángulo ──────────────────────────────────────────────────────────
        { "10 rad",      "10 rad / 10 radians -> 572.96 deg / 572.96 degrees"                       },
        { "10 radian",   "10 rad / 10 radians -> 572.96 deg / 572.96 degrees"                       },
        { "10 radians",  "10 rad / 10 radians -> 572.96 deg / 572.96 degrees"                       },
        { "10 deg",      "10 deg / 10 degrees -> 0.1745329252 rad / 0.1745329252 radians"           },
        { "10 degree",   "10 deg / 10 degrees -> 0.1745329252 rad / 0.1745329252 radians"           },
        { "10 degrees",  "10 deg / 10 degrees -> 0.1745329252 rad / 0.1745329252 radians"           },
        { "10 grad",     "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 gradian",  "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 gradians", "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 arcmin",   "10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                 },
        { "10 arcminute","10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                 },
        { "10 arcminutes","10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                 },
        // ── Área ────────────────────────────────────────────────────────────
        { "10 m2",       "10 m2 -> 107.64 sqft"                                                     },
        { "10 sqft",     "10 sqft -> 0.9290304 m2"                                                  },
        { "10 ha",       "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 hectare",  "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 hectares", "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 acre",     "10 acre / 10 acres -> 4.05 ha / 4.05 hectares"                            },
        { "10 acres",    "10 acre / 10 acres -> 4.05 ha / 4.05 hectares"                            },
    };

    [Theory]
    [MemberData(nameof(DefaultConversionCases))]
    public void DefaultConversion_Summary(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    // ── Alias y formas canónicas ─────────────────────────────────────────────
    // Cualquier sinónimo (10h, 10hour, 10 hours, 10 foot, 10 feet, 10 mile, etc.)
    // debe normalizarse a la unidad canónica y producir el mismo resultado.

    public static TheoryData<string, string> UnitAliasCases => new() {
        // ── Tiempo — formas largas (auto-reverse de longNames) ───────────────
        { "10 hour",        "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 second",      "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"        },
        { "10 millisecond", "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"          },
        // Tiempo — plurales (tokenAliases)
        { "10 hours",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 seconds",     "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"        },
        { "10 milliseconds","10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"          },
        { "10 minutes",     "10 min / 10 minutes -> 600 s / 600 seconds"                },
        { "10 days",        "10 day / 10 days -> 240 h / 240 hours"                     },
        { "10 weeks",       "10 week / 10 weeks -> 70 day / 70 days"                    },
        { "10 months",      "10 month / 10 months -> 304.38 day / 304.38 days"          },
        { "10 years",       "10 year / 10 years -> 3652.5 day / 3652.5 days"          },
        // Tiempo — capitalización variada
        { "10 Hour",        "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 Hours",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 HOURS",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        // ── Temperatura — formas largas y capitalización ─────────────────────
        { "100 celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 fahrenheit", "100 °F / 100 fahrenheit -> 37.78 °C / 37.78 celsius"        },
        { "100 Celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 FAHRENHEIT", "100 °F / 100 fahrenheit -> 37.78 °C / 37.78 celsius"        },
        // ── Longitud — formas largas y plurales ──────────────────────────────
        { "10 foot",        "10 ft / 10 feet -> 3.05 m / 3.05 meters"                   },
        { "10 feet",        "10 ft / 10 feet -> 3.05 m / 3.05 meters"                   },
        { "10 inch",        "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"           },
        { "10 inches",      "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"           },
        { "10 mile",        "10 mi / 10 miles -> 16.09 km / 16.09 kilometers"           },
        { "10 miles",       "10 mi / 10 miles -> 16.09 km / 16.09 kilometers"           },
        { "10 yards",       "10 yard / 10 yards -> 9.14 m / 9.14 meters"                },
        // ── Masa — formas largas y plurales ──────────────────────────────────
        { "10 ounce",       "10 oz / 10 ounces -> 283.5 g / 283.5 grams"                },
        { "10 ounces",      "10 oz / 10 ounces -> 283.5 g / 283.5 grams"                },
        { "10 pound",       "10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"             },
        { "10 pounds",      "10 lb / 10 pounds -> 4.54 kg / 4.54 kilograms"             },
        // ── Volumen — formas largas y plurales ───────────────────────────────
        { "10 liter",       "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"            },
        { "10 litre",       "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"            },
        { "10 liters",      "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"            },
        { "10 litres",      "10 L / 10 litres -> 2.64 gallon / 2.64 gallons"            },
        { "10 gallons",     "10 gallon / 10 gallons -> 37.85 L / 37.85 litres"          },
        // ── Área — tokenAlias ha→hectare ─────────────────────────────────────
        { "10 hectare",     "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"           },
        { "10 hectares",    "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"           },
        // ── Potencia — formas largas ──────────────────────────────────────────
        { "10 horsepower",  "10 hp / 10 horsepowers -> 7.46 kW / 7.46 kilowatts"       },
        { "10 horsepowers", "10 hp / 10 horsepowers -> 7.46 kW / 7.46 kilowatts"       },
        // ── Masa — formas largas y plurales ──────────────────────────────────
        { "10 gram",        "10 g / 10 grams -> 0.3527396195 oz / 0.3527396195 ounces"    },
        { "10 tonne",       "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"              },
        // ── Electricidad — formas largas ──────────────────────────────────────
        { "10 watt",        "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                },
        { "10 volt",        "10 V / 10 volts -> 10000 mV / 10000 millivolts"             },
        { "10 ampere",      "10 A / 10 amperes -> 10000 mA / 10000 milliamperes"         },
        { "10 henry",       "10 H / 10 henrys -> 10000 mH / 10000 millihenrys"           },
        { "10 tesla",       "10 T / 10 teslas -> 10000 mT / 10000 milliteslas"           },
        // ── Presión — formas largas ───────────────────────────────────────────
        { "10 pascal",      "10 Pa / 10 pascals -> 0.001450377377 psi"                    },
        { "10 atmosphere",  "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"           },
        // ── Longitud — formas largas ──────────────────────────────────────────
        { "10 meter",       "10 m / 10 meters -> 32.81 ft / 32.81 feet"                   },
        { "10 meters",      "10 m / 10 meters -> 32.81 ft / 32.81 feet"                   },
        { "10 kilometer",   "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"             },
        { "10 kilometers",  "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"             },
        { "10 centimeter",  "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"             },
        { "10 centimeters", "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"             },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches" },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches" },
        // ── Volumen — formas largas ───────────────────────────────────────────
        { "10 gal",         "10 gallon / 10 gallons -> 37.85 L / 37.85 litres"            },
        // ── Fuerza — formas largas y poundforce ──────────────────────────────
        { "10 newton",      "10 N / 10 newtons -> 2.25 lbf / 2.25 pound-forces"          },
        { "10 newtons",     "10 N / 10 newtons -> 2.25 lbf / 2.25 pound-forces"          },
        { "10poundforce",   "10 lbf / 10 pound-forces -> 44.48 N / 44.48 newtons"        },
        { "10 poundforces", "10 lbf / 10 pound-forces -> 44.48 N / 44.48 newtons"        },
        // ── Datos — formas largas y plurales ─────────────────────────────────
        { "10 byte",        "10 B / 10 bytes -> 0.01 kB / 0.01 kilobytes"                },
        { "10 bytes",       "10 B / 10 bytes -> 0.01 kB / 0.01 kilobytes"                },
        { "10 kilobyte",    "10 kB / 10 kilobytes -> 0.01 MB / 0.01 megabytes"           },
        { "10 kilobytes",   "10 kB / 10 kilobytes -> 0.01 MB / 0.01 megabytes"           },
        { "10 megabyte",    "10 MB / 10 megabytes -> 0.01 GB / 0.01 gigabytes"           },
        { "10 megabytes",   "10 MB / 10 megabytes -> 0.01 GB / 0.01 gigabytes"           },
        { "10 gigabyte",    "10 GB / 10 gigabytes -> 0.01 TB / 0.01 terabytes"           },
        { "10 gigabytes",   "10 GB / 10 gigabytes -> 0.01 TB / 0.01 terabytes"           },
        { "10 terabyte",    "10 TB / 10 terabytes -> 1000 GB / 1000 gigabytes"           },
        { "10 terabytes",   "10 TB / 10 terabytes -> 1000 GB / 1000 gigabytes"           },
        // ── Electricidad adicional — plurales ─────────────────────────────────
        { "10 ohms",        "10 ohm / 10 ohms -> 0.01 kohm / 0.01 kilohms"                },
        { "10 moles",       "10 mol / 10 moles -> 10000 mmol / 10000 millimoles"         },
        { "10 siemens",     "10 S / 10 siemens -> 10000 mS / 10000 millisiemens"         },
        // ── Volumen menor — plurales ──────────────────────────────────────────
        { "10 pints",       "10 pint / 10 pints -> 20 cup / 20 cups"                     },
        { "10 quarts",      "10 quart / 10 quarts -> 20 pint / 20 pints"                 },
        { "10 cups",        "10 cup / 10 cups -> 80 floz / 80 fluid ounces"              },
        { "10 tablespoons", "10 tablespoon / 10 tablespoons -> 30 teaspoon / 30 teaspoons" },
        { "10 teaspoons",   "10 teaspoon / 10 teaspoons -> 50 mL / 50 millilitres"        },
        // ── Tiempo — decade alias ─────────────────────────────────────────────
        { "10 decades",     "10 decade / 10 decades -> 100 year / 100 years"             },
        // ── Datos — TB longname ────────────────────────────────────────────────
        { "10 terabytes",   "10 TB / 10 terabytes -> 10000 GB / 10000 gigabytes"          },
        // ── Volumen menor — singulares canónicos ──────────────────────────────
        { "10 tablespoon",  "10 tablespoon / 10 tablespoons -> 30 teaspoon / 30 teaspoons" },
        { "10 teaspoon",    "10 teaspoon / 10 teaspoons -> 50 mL / 50 millilitres"         },
        // ── Fuerza — poundforce singular ──────────────────────────────────────
        { "10 poundforce",  "10 lbf / 10 pound-forces -> 44.48 N / 44.48 newtons"         },
        // ── Singular (1 unit) — long name suprimido cuando símbolo == longName ──
        // Regla: long name solo se muestra si aporta información (símbolo ≠ longName).
        // Suprimidos (símbolo==longName en singular): pint, quart, cup, ohm, year, decade, tablespoon, teaspoon.
        // Visibles: B/byte, kB/kilobyte, S/siemens, mol/mole, lbf/pound-force, hp/horsepower, floz/fluid ounce, etc.
        // ── Datos ─────────────────────────────────────────────────────────────
        { "1 B",           "1 B / 1 byte -> 0.001 kB / 0.001 kilobytes"              },
        { "1 byte",        "1 B / 1 byte -> 0.001 kB / 0.001 kilobytes"              },
        { "1 bytes",       "1 B / 1 byte -> 0.001 kB / 0.001 kilobytes"              },
        { "1 kB",          "1 kB / 1 kilobyte -> 0.001 MB / 0.001 megabytes"         },
        { "1 kilobyte",    "1 kB / 1 kilobyte -> 0.001 MB / 0.001 megabytes"         },
        { "1 kilobytes",   "1 kB / 1 kilobyte -> 0.001 MB / 0.001 megabytes"         },
        { "1 MB",          "1 MB / 1 megabyte -> 0.001 GB / 0.001 gigabytes"         },
        { "1 megabyte",    "1 MB / 1 megabyte -> 0.001 GB / 0.001 gigabytes"         },
        { "1 megabytes",   "1 MB / 1 megabyte -> 0.001 GB / 0.001 gigabytes"         },
        { "1 GB",          "1 GB / 1 gigabyte -> 0.001 TB / 0.001 terabytes"         },
        { "1 gigabyte",    "1 GB / 1 gigabyte -> 0.001 TB / 0.001 terabytes"         },
        { "1 gigabytes",   "1 GB / 1 gigabyte -> 0.001 TB / 0.001 terabytes"         },
        { "1 TB",          "1 TB / 1 terabyte -> 1000 GB / 1000 gigabytes"           },
        { "1 terabyte",    "1 TB / 1 terabyte -> 1000 GB / 1000 gigabytes"           },
        { "1 terabytes",   "1 TB / 1 terabyte -> 1000 GB / 1000 gigabytes"           },
        // ── Electromagnetismo adicional ────────────────────────────────────────
        { "1 ohm",         "1 ohm -> 0.001 kohm / 0.001 kilohms"                     },
        { "1 ohms",        "1 ohm -> 0.001 kohm / 0.001 kilohms"                     },
        { "1 mol",         "1 mol / 1 mole -> 1000 mmol / 1000 millimoles"           },
        { "1 moles",       "1 mol / 1 mole -> 1000 mmol / 1000 millimoles"           },
        { "1 S",           "1 S / 1 siemens -> 1000 mS / 1000 millisiemens"          },
        { "1 siemens",     "1 S / 1 siemens -> 1000 mS / 1000 millisiemens"          },
        // ── Tiempo adicional ──────────────────────────────────────────────────
        { "1 year",        "1 year -> 365.25 day / 365.25 days"                       },
        { "1 years",       "1 year -> 365.25 day / 365.25 days"                       },
        { "1 decade",      "1 decade -> 10 year / 10 years"                           },
        { "1 decades",     "1 decade -> 10 year / 10 years"                           },
        // ── Volumen menor ─────────────────────────────────────────────────────
        { "1 pint",        "1 pint -> 2 cup / 2 cups"                                 },
        { "1 pints",       "1 pint -> 2 cup / 2 cups"                                 },
        { "1 quart",       "1 quart -> 2 pint / 2 pints"                              },
        { "1 quarts",      "1 quart -> 2 pint / 2 pints"                              },
        { "1 cup",         "1 cup -> 8 floz / 8 fluid ounces"                         },
        { "1 cups",        "1 cup -> 8 floz / 8 fluid ounces"                         },
        { "1 floz",        "1 floz / 1 fluid ounce -> 29.57 mL / 29.57 millilitres"  },
        { "1 tbsp",        "1 tablespoon -> 3 teaspoon / 3 teaspoons"                 },
        { "1 tablespoon",  "1 tablespoon -> 3 teaspoon / 3 teaspoons"                 },
        { "1 tablespoons", "1 tablespoon -> 3 teaspoon / 3 teaspoons"                 },
        { "1 tsp",         "1 teaspoon -> 5 mL / 5 millilitres"                       },
        { "1 teaspoon",    "1 teaspoon -> 5 mL / 5 millilitres"                       },
        { "1 teaspoons",   "1 teaspoon -> 5 mL / 5 millilitres"                       },
        { "1 cc",          "1 cc / 1 cubic centimeter -> 1 mL / 1 millilitre"         },
        // ── Fuerza ────────────────────────────────────────────────────────────
        { "1 lbf",         "1 lbf / 1 pound-force -> 4.45 N / 4.45 newtons"          },
        { "1 poundforce",  "1 lbf / 1 pound-force -> 4.45 N / 4.45 newtons"          },
        { "1 poundforces", "1 lbf / 1 pound-force -> 4.45 N / 4.45 newtons"          },
        // ── Potencia ─────────────────────────────────────────────────────────
        { "1 horsepower",  "1 hp / 1 horsepower -> 0.7456998715 kW / 0.7456998715 kilowatts" },
        { "1 horsepowers", "1 hp / 1 horsepower -> 0.7456998715 kW / 0.7456998715 kilowatts" },
        // ── Smoke test 0.01 — detección de errores de redondeo y formato ─────
        // Nota: el sistema normaliza el "from" al prefijo SI más conveniente
        // (0.01 V → 10 mV, 0.01 s → 10 ms, 0.01 km → 10 m, etc.)
        // ── Temperatura ───────────────────────────────────────────────────────
        { "0.01c",      "10 mdegC / 10 millicelsius -> 32.02 °F / 32.02 fahrenheit"      },
        { "0.01f",      "10 mdegF / 10 millifahrenheit -> -17.77 °C / -17.77 celsius"    },
        // La parte decimal es tan pequeña que se absorbe en el redondeo a 2 decimales → "32"
        { "0.00001c",   "10 udegC / 10 microcelsius -> 32 °F / 32 fahrenheit"            },
        // ── Electricidad ──────────────────────────────────────────────────────
        { "0.01V",      "10 mV / 10 millivolts -> 10 mV / 10 millivolts"                 },
        { "0.01A",      "10 mA / 10 milliamperes -> 10 mA / 10 milliamperes"             },
        { "0.01W",      "10 mW / 10 milliwatts -> 1e-5 kW / 1e-5 kilowatts"             },
        { "0.01H",      "10 mH / 10 millihenrys -> 10 mH / 10 millihenrys"              },
        { "0.01T",      "10 mT / 10 milliteslas -> 10 mT / 10 milliteslas"              },
        { "0.01C",      "10 mC / 10 millicoulombs -> 10 mC / 10 millicoulombs"          },
        { "0.01F",      "10 mF / 10 millifarads -> 10000 uF / 10000 microfarads"        },
        // ── Tiempo ────────────────────────────────────────────────────────────
        { "0.01h",      "0.01 h / 0.01 hours -> 0.6 min / 0.6 minutes"                   },
        { "0.01 day",   "0.01 day / 0.01 days -> 0.24 h / 0.24 hours"                    },
        { "0.01 min",   "0.01 min / 0.01 minutes -> 0.6 s / 0.6 seconds"                 },
        { "0.01s",      "10 ms / 10 milliseconds -> 10 ms / 10 milliseconds"              },
        { "0.01ms",     "10 us / 10 microseconds -> 1e-5 s / 1e-5 seconds"               },
        { "0.01Ms",     "10 ks / 10 kiloseconds -> 2.78 h / 2.78 hours"                  },
        // ── Masa ──────────────────────────────────────────────────────────────
        { "0.01t",      "10 mt / 10 millitonnes -> 10 kg / 10 kilograms"                 },
        { "0.01 g",     "10 mg / 10 milligrams -> 3.527396195e-4 oz / 3.527396195e-4 ounces" },
        { "0.01 oz",    "0.01 oz / 0.01 ounces -> 0.2834952313 g / 0.2834952313 grams"  },
        { "0.01 lb",    "0.01 lb / 0.01 pounds -> 0.0045359237 kg / 0.0045359237 kilograms" },
        // ── Longitud ──────────────────────────────────────────────────────────
        { "0.01 m",     "10 mm / 10 millimeters -> 0.03280839895 ft / 0.03280839895 feet" },
        { "0.01 km",    "10 m / 10 meters -> 0.006213711922 mile / 0.006213711922 miles" },
        { "0.01 cm",    "100 um / 100 micrometers -> 0.003937007874 in / 0.003937007874 inches" },
        { "0.01 mm",    "10 um / 10 micrometers -> 3.937007874e-4 in / 3.937007874e-4 inches" },
        { "0.01 ft",    "0.01 ft / 0.01 feet -> 0.003048 m / 0.003048 meters"            },
        { "0.01 in",    "0.01 in / 0.01 inches -> 0.0254 cm / 0.0254 centimeters"        },
        { "0.01 yard",  "0.01 yard / 0.01 yards -> 0.009144 m / 0.009144 meters"         },
        { "0.01 mi",    "0.01 mi / 0.01 miles -> 0.01609344 km / 0.01609344 kilometers"  },
        // ── Volumen ───────────────────────────────────────────────────────────
        { "0.01 l",     "10 mL / 10 millilitres -> 0.002641720373 gallon / 0.002641720373 gallons" },
        { "0.01 gal",   "0.01 gallon / 0.01 gallons -> 0.03785412 L / 0.03785412 litres" },
        // ── Presión ───────────────────────────────────────────────────────────
        { "0.01 Pa",    "10 mPa -> 1.450377377e-6 psi"                                   },
        { "0.01 bar",   "10 mbar -> 0.1450377377 psi"                                    },
        { "0.01 atm",   "0.01 atm / 0.01 atmospheres -> 0.0101325 bar / 0.0101325 bars"  },
        { "0.01 psi",   "0.01 psi -> 6.894757293e-4 bar / 6.894757293e-4 bars"           },
        { "0.01 torr",  "0.01 torr -> 0.01 mmHg"                                         },
        { "0.01 mmHg",  "0.01 mmHg -> 0.00133322 kPa"                                   },
        // ── Fuerza ────────────────────────────────────────────────────────────
        { "0.01 N",     "10 mN / 10 millinewtons -> 0.002248089431 lbf / 0.002248089431 pound-forces" },
        { "0.01 lbf",   "0.01 lbf / 0.01 pound-forces -> 0.04448221615 N / 0.04448221615 newtons" },
        { "0.01 kgf",   "0.01 kgf / 0.01 kilogram-forces -> 0.0980665 N / 0.0980665 newtons" },
        { "0.01 dyn",   "10 mdyn / 10 millidynes -> 1e-4 mN / 1e-4 millinewtons"        },
        // ── Energía ───────────────────────────────────────────────────────────
        { "0.01 J",     "10 mJ / 10 millijoules -> 1e-5 kJ / 1e-5 kilojoules"             },
        { "0.01 kJ",    "10 J / 10 joules -> 0.002777777778 Wh"                                },
        { "0.01 Wh",    "10 mWh -> 0.036 kJ / 0.036 kilojoules"                         },
        { "0.01 eV",    "10 meV / 10 millielectronvolts -> 1.602176565e-21 J / 1.602176565e-21 joules" },
        { "0.01 erg",   "10 merg -> 1e-9 J / 1e-9 joules"                               },
        // ── Potencia ──────────────────────────────────────────────────────────
        { "0.01 hp",    "0.01 hp / 0.01 horsepowers -> 0.007456998715 kW / 0.007456998715 kilowatts" },
        // ── Datos ─────────────────────────────────────────────────────────────
        { "0.01 B",     "0.01 B / 0.01 bytes -> 1e-5 kB / 1e-5 kilobytes"                 },
        { "0.01 kB",    "10 B / 10 bytes -> 1e-5 MB / 1e-5 megabytes"                   },
        { "0.01 MB",    "10 kB / 10 kilobytes -> 1e-5 GB / 1e-5 gigabytes"              },
        { "0.01 GB",    "10 MB / 10 megabytes -> 1e-5 TB / 1e-5 terabytes"              },
        { "0.01 TB",    "10 GB / 10 gigabytes -> 10 GB / 10 gigabytes"                  },
        // ── Electromagnetismo adicional ────────────────────────────────────────
        { "0.01 ohm",   "10 mohm / 10 milliohms -> 1e-5 kohm / 1e-5 kilohms"             },
        { "0.01 mol",   "10 mmol / 10 millimoles -> 10 mmol / 10 millimoles"             },
        { "0.01 S",     "10 mS / 10 millisiemens -> 10 mS / 10 millisiemens"            },
        // ── Tiempo adicional ──────────────────────────────────────────────────
        { "0.01 year",   "0.01 year / 0.01 years -> 3.65 day / 3.65 days"               },
        { "0.01 decade", "0.01 decade / 0.01 decades -> 0.1 year / 0.1 years"           },
        // ── Volumen menor ─────────────────────────────────────────────────────
        { "0.01 pint",   "0.01 pint / 0.01 pints -> 0.02000000423 cup / 0.02000000423 cups"    },
        { "0.01 quart",  "0.01 quart / 0.01 quarts -> 0.01999999789 pint / 0.01999999789 pints" },
        { "0.01 cup",    "0.01 cup / 0.01 cups -> 0.07999998647 floz / 0.07999998647 fluid ounces" },
        { "0.01 floz",   "0.01 floz / 0.01 fluid ounces -> 0.2957353 mL / 0.2957353 millilitres" },
        { "0.01 tbsp",   "0.01 tablespoon / 0.01 tablespoons -> 0.03 teaspoon / 0.03 teaspoons" },
        { "0.01 tsp",    "0.01 teaspoon / 0.01 teaspoons -> 0.05 mL / 0.05 millilitres" },
        { "0.01 cc",     "0.01 cc / 0.01 cubic centimeters -> 0.01 mL / 0.01 millilitres" },
        // ── Ángulo ────────────────────────────────────────────────────────────
        { "0.01 rad",   "10 mrad / 10 milliradians -> 0.5729577951 deg / 0.5729577951 degrees" },
        { "0.01 deg",   "10 mdeg / 10 millidegrees -> 1.745329252e-4 rad / 1.745329252e-4 radians" },
        { "0.01 grad",  "10 mgrad / 10 milligradians -> 0.009 deg / 0.009 degrees"       },
        { "0.01 arcmin","0.01 arcmin / 0.01 arcminutes -> 0.6 arcsec / 0.6 arcseconds"  },
        // ── Área ──────────────────────────────────────────────────────────────
        { "0.01 m2",    "10000 mm2 -> 0.1076391042 sqft"                                 },
        { "0.01 sqft",  "0.01 sqft -> 9.290304e-4 m2"                                    },
        { "0.01 ha",    "0.01 ha / 0.01 hectares -> 0.0247105163 acre / 0.0247105163 acres" },
        { "0.01 acre",  "0.01 acre / 0.01 acres -> 0.00404686 ha / 0.00404686 hectares"  },
    };

    [Theory]
    [MemberData(nameof(UnitAliasCases))]
    public void UnitAlias_NormalizesToCanonical(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    // ── Normalización de prefijo SI en el "from" ─────────────────────────────
    // math.js (vía math.format) reformatea el valor de entrada al prefijo SI
    // más conveniente cuando el coeficiente es < 1 en la unidad original.
    // Solo ocurre hacia ABAJO (0.001 V → 1 mV); no ocurre hacia ARRIBA
    // (1000 m permanece como 1000 m, no se convierte a 1 km).
    // Las unidades imperiales y no-SI (oz, ft, atm, psi, hp, acre…) NO se
    // normalizan nunca — conservan el valor tal como lo escribió el usuario.
    // Ref: math.format() → unit.simplify() en math.js.

    public static TheoryData<string, string, string> FromPrefixNormalizationCases => new() {
        // query              from esperado               long suffix (o "" si null)
        // ── SI: normaliza hacia abajo cuando coeff < 1 ───────────────────────
        { "0.001 V",    "1 mV",         "millivolt"      },
        { "0.001 A",    "1 mA",         "milliampere"    },
        { "0.001 s",    "1 ms",         "millisecond"    },
        { "0.001 m",    "1 mm",         "millimeter"     },
        { "0.001 g",    "1 mg",         "milligram"      },
        { "0.001 J",    "1 mJ",         "millijoule"     },
        { "0.001 W",    "1 mW",         "milliwatt"      },
        { "0.001 Pa",   "1 mPa",        ""               },  // Pa no tiene longName en LONG prefix group
        // ── SI: NO normaliza hacia arriba cuando coeff >= 1 ──────────────────
        { "1000 m",     "1000 m",       "meters"         },
        { "1000 g",     "1000 g",       "grams"          },
        { "1000 W",     "1000 W",       "watts"          },
        { "1000 V",     "1000 V",       "volts"          },
        // ── No-SI / imperial: nunca normaliza ─────────────────────────────────
        { "0.001 ft",   "0.001 ft",     "feet"           },
        { "0.001 oz",   "0.001 oz",     "ounces"         },
        { "0.001 atm",  "0.001 atm",    "atmospheres"    },
        { "0.001 psi",  "0.001 psi",    ""               },  // psi no tiene longName
        { "0.001 hp",   "0.001 hp",     "horsepowers"    },
        { "0.001 acre", "0.001 acre",   "acres"          },
    };

    [Theory]
    [MemberData(nameof(FromPrefixNormalizationCases))]
    public void FromUnit_AutoNormalizesToBestSIPrefix(string query, string expectedFromShort, string expectedFromLongSuffix) {
        var item = GetConversionItem(query);
        Assert.Equal(expectedFromShort, item.FromShort);
        if (!string.IsNullOrEmpty(expectedFromLongSuffix))
            Assert.EndsWith(expectedFromLongSuffix, item.FromLong);
        else
            Assert.Null(item.FromLong);
    }
}

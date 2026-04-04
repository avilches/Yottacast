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
        { "10f",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        // 10F is Faraday
        { "10ºf",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"          },
        { "10ºF",      "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"          },
        { "10 degc",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 DEGC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degf",  "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
        { "10 DEGF",  "10 °F / 10 fahrenheit -> -12.22222222 °C / -12.22222222 celsius"           },
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
        { "10Ms",          "10 Ms / 10 megaseconds -> 2777.777778 h / 2777.777778 hours"               },
        { "10megasecond",  "10 Ms / 10 megaseconds -> 2777.777778 h / 2777.777778 hours"               },
        { "10megaseconds", "10 Ms / 10 megaseconds -> 2777.777778 h / 2777.777778 hours"               },
        // ── Masa ────────────────────────────────────────────────────────────
        { "10t",      "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"                            },
        { "10tonnes", "10 t / 10 tonnes -> 10000 kg / 10000 kilograms"                            },
        { "10 g",     "10 g / 10 grams -> 0.3527396195 oz / 0.3527396195 ounces"                 },
        { "10 grams", "10 g / 10 grams -> 0.3527396195 oz / 0.3527396195 ounces"                 },
        { "10 oz",    "10 oz / 10 ounces -> 283.4952313 g / 283.4952313 grams"                   },
        { "10 ounces","10 oz / 10 ounces -> 283.4952313 g / 283.4952313 grams"                   },
        { "10 lb",    "10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"                  },
        { "10 lbs",   "10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"                  },
        { "10 pound", "10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"                  },
        { "10 pounds","10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"                  },
        // ── Longitud ────────────────────────────────────────────────────────
        { "10 m",           "10 m / 10 meters -> 32.80839895 ft / 32.80839895 feet"                    },
        { "10 meter",       "10 m / 10 meters -> 32.80839895 ft / 32.80839895 feet"                    },
        { "10 meters",      "10 m / 10 meters -> 32.80839895 ft / 32.80839895 feet"                    },
        { "10 km",          "10 km / 10 kilometers -> 6.213711922 mile / 6.213711922 miles"             },
        { "10 kilometer",   "10 km / 10 kilometers -> 6.213711922 mile / 6.213711922 miles"             },
        { "10 kilometers",  "10 km / 10 kilometers -> 6.213711922 mile / 6.213711922 miles"             },
        { "10 cm",          "10 cm / 10 centimeters -> 3.937007874 in / 3.937007874 inches"            },
        { "10 centimeter",  "10 cm / 10 centimeters -> 3.937007874 in / 3.937007874 inches"            },
        { "10 centimeters", "10 cm / 10 centimeters -> 3.937007874 in / 3.937007874 inches"            },
        { "10 mm",          "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches"          },
        { "10 ft",          "10 ft / 10 feet -> 3.048 m / 3.048 meters"                                },
        { "10 feet",        "10 ft / 10 feet -> 3.048 m / 3.048 meters"                                },
        { "1 foot",         "1 ft / 1 foot -> 0.3048 m / 0.3048 meters"                                },
        { "10 in",          "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 inch",        "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 inches",      "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"                          },
        { "10 yard",        "10 yard / 10 yards -> 9.144 m / 9.144 meters"                             },
        { "10 yards",       "10 yard / 10 yards -> 9.144 m / 9.144 meters"                             },
        { "10 mi",          "10 mi / 10 miles -> 16.09344 km / 16.09344 kilometers"                    },
        { "10 mile",        "10 mi / 10 miles -> 16.09344 km / 16.09344 kilometers"                    },
        { "10 miles",       "10 mi / 10 miles -> 16.09344 km / 16.09344 kilometers"                    },
        // ── Volumen ─────────────────────────────────────────────────────────
        { "10 l",     "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons"             },
        { "10 L",     "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons"             },
        { "10 gal",   "10 gallon / 10 gallons -> 37.85412 L / 37.85412 litres"                   },
        { "10 gallon","10 gallon / 10 gallons -> 37.85412 L / 37.85412 litres"                   },
        { "10 gallons","10 gallon / 10 gallons -> 37.85412 L / 37.85412 litres"                   },
        // ── Presión ─────────────────────────────────────────────────────────
        { "10 Pa",         "10 Pa / 10 pascals -> 0.001450377377 psi"                              },
        { "10 pascals",    "10 Pa / 10 pascals -> 0.001450377377 psi"                              },
        { "10 bar",        "10 bar / 10 bars -> 145.0377377 psi"                                   },
        { "10 atm",        "10 atm / 10 atmospheres -> 10.1325 bar / 10.1325 bars"                 },
        { "10 atmosphere", "10 atm / 10 atmospheres -> 10.1325 bar / 10.1325 bars"                 },
        { "10 atmospheres","10 atm / 10 atmospheres -> 10.1325 bar / 10.1325 bars"                 },
        { "10 psi",   "10 psi -> 0.6894757293 bar / 0.6894757293 bars"                           },
        { "10 torr",  "10 torr -> 10 mmHg"                                                       },
        { "10 mmHg",  "10 mmHg -> 1.33322 kPa"                                                   },
        // ── Fuerza ──────────────────────────────────────────────────────────
        { "10 N",       "10 N / 10 newtons -> 2.248089431 lbf / 2.248089431 pound-forces"       },
        { "10 newton",  "10 N / 10 newtons -> 2.248089431 lbf / 2.248089431 pound-forces"       },
        { "10 newtons", "10 N / 10 newtons -> 2.248089431 lbf / 2.248089431 pound-forces"       },
        { "10 lbf",   "10 lbf / 10 pound-forces -> 44.48221615 N / 44.48221615 newtons"         },
        { "10 kgf",   "10 kgf / 10 kilogram-forces -> 98.0665 N / 98.0665 newtons"              },
        { "10 dyn",   "10 dyn / 10 dynes -> 0.1 mN / 0.1 millinewtons"                          },
        // ── Energía ─────────────────────────────────────────────────────────
        { "10 J",     "10 J / 10 joules -> 0.009478171203 BTU"                                   },
        { "10 kJ",    "10 kJ / 10 kilojoules -> 9.478171203 BTU"                                 },
        { "10 BTU",   "10 BTU -> 10.55055853 kJ / 10.55055853 kilojoules"                        },
        { "10 Wh",    "10 Wh -> 36 kJ / 36 kilojoules"                                           },
        { "10 eV",    "10 eV / 10 electronvolts -> 1.602176565e-18 J / 1.602176565e-18 joules"  },
        { "10 erg",   "10 erg -> 1e-6 J / 1e-6 joules"                                           },
        // ── Potencia ────────────────────────────────────────────────────────
        { "10 hp",          "10 hp / 10 horsepowers -> 7.456998715 kW / 7.456998715 kilowatts"        },
        { "10 horsepower",  "10 hp / 10 horsepowers -> 7.456998715 kW / 7.456998715 kilowatts"        },
        { "10 horsepowers", "10 hp / 10 horsepowers -> 7.456998715 kW / 7.456998715 kilowatts"        },
        // ── Datos ───────────────────────────────────────────────────────────
        { "10 B",     "10 B -> 0.01 kB"                                                           },
        { "10 kB",    "10 kB -> 0.01 MB"                                                          },
        { "10 MB",    "10 MB -> 0.01 GB"                                                          },
        // ── Ángulo ──────────────────────────────────────────────────────────
        { "10 rad",      "10 rad / 10 radians -> 572.9577951 deg / 572.9577951 degrees"             },
        { "10 radian",   "10 rad / 10 radians -> 572.9577951 deg / 572.9577951 degrees"             },
        { "10 radians",  "10 rad / 10 radians -> 572.9577951 deg / 572.9577951 degrees"             },
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
        { "10 m2",       "10 m2 -> 107.6391042 sqft"                                                },
        { "10 sqft",     "10 sqft -> 0.9290304 m2"                                                  },
        { "10 ha",       "10 ha / 10 hectares -> 24.7105163 acre / 24.7105163 acres"                },
        { "10 hectare",  "10 ha / 10 hectares -> 24.7105163 acre / 24.7105163 acres"                },
        { "10 hectares", "10 ha / 10 hectares -> 24.7105163 acre / 24.7105163 acres"                },
        { "10 acre",     "10 acre / 10 acres -> 4.04686 ha / 4.04686 hectares"                      },
        { "10 acres",    "10 acre / 10 acres -> 4.04686 ha / 4.04686 hectares"                      },
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
        { "10 months",      "10 month / 10 months -> 304.375 day / 304.375 days"        },
        { "10 years",       "10 year -> 3652.5 day / 3652.5 days"                       },
        // Tiempo — capitalización variada
        { "10 Hour",        "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 Hours",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        { "10 HOURS",       "10 h / 10 hours -> 600 min / 600 minutes"                  },
        // ── Temperatura — formas largas y capitalización ─────────────────────
        { "100 celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 fahrenheit", "100 °F / 100 fahrenheit -> 37.77777778 °C / 37.77777778 celsius" },
        { "100 Celsius",    "100 °C / 100 celsius -> 212 °F / 212 fahrenheit"           },
        { "100 FAHRENHEIT", "100 °F / 100 fahrenheit -> 37.77777778 °C / 37.77777778 celsius" },
        // ── Longitud — formas largas y plurales ──────────────────────────────
        { "10 foot",        "10 ft / 10 feet -> 3.048 m / 3.048 meters"                 },
        { "10 feet",        "10 ft / 10 feet -> 3.048 m / 3.048 meters"                 },
        { "10 inch",        "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"           },
        { "10 inches",      "10 in / 10 inches -> 25.4 cm / 25.4 centimeters"           },
        { "10 mile",        "10 mi / 10 miles -> 16.09344 km / 16.09344 kilometers"     },
        { "10 miles",       "10 mi / 10 miles -> 16.09344 km / 16.09344 kilometers"     },
        { "10 yards",       "10 yard / 10 yards -> 9.144 m / 9.144 meters"              },
        // ── Masa — formas largas y plurales ──────────────────────────────────
        { "10 ounce",       "10 oz / 10 ounces -> 283.4952313 g / 283.4952313 grams"    },
        { "10 ounces",      "10 oz / 10 ounces -> 283.4952313 g / 283.4952313 grams"    },
        { "10 pound",       "10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"   },
        { "10 pounds",      "10 lb / 10 pounds -> 4.5359237 kg / 4.5359237 kilograms"   },
        // ── Volumen — formas largas y plurales ───────────────────────────────
        { "10 liter",       "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons" },
        { "10 litre",       "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons" },
        { "10 liters",      "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons" },
        { "10 litres",      "10 L / 10 litres -> 2.641720373 gallon / 2.641720373 gallons" },
        { "10 gallons",     "10 gallon / 10 gallons -> 37.85412 L / 37.85412 litres"      },
        // ── Área — tokenAlias ha→hectare ─────────────────────────────────────
        { "10 hectare",     "10 ha / 10 hectares -> 24.7105163 acre / 24.7105163 acres"   },
        { "10 hectares",    "10 ha / 10 hectares -> 24.7105163 acre / 24.7105163 acres"   },
        // ── Potencia — formas largas ──────────────────────────────────────────
        { "10 horsepower",  "10 hp / 10 horsepowers -> 7.456998715 kW / 7.456998715 kilowatts" },
        { "10 horsepowers", "10 hp / 10 horsepowers -> 7.456998715 kW / 7.456998715 kilowatts" },
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
        { "10 atmosphere",  "10 atm / 10 atmospheres -> 10.1325 bar / 10.1325 bars"       },
        // ── Longitud — formas largas ──────────────────────────────────────────
        { "10 meter",       "10 m / 10 meters -> 32.80839895 ft / 32.80839895 feet"       },
        { "10 meters",      "10 m / 10 meters -> 32.80839895 ft / 32.80839895 feet"       },
        { "10 kilometer",   "10 km / 10 kilometers -> 6.213711922 mile / 6.213711922 miles" },
        { "10 kilometers",  "10 km / 10 kilometers -> 6.213711922 mile / 6.213711922 miles" },
        { "10 centimeter",  "10 cm / 10 centimeters -> 3.937007874 in / 3.937007874 inches" },
        { "10 centimeters", "10 cm / 10 centimeters -> 3.937007874 in / 3.937007874 inches" },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches" },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.3937007874 in / 0.3937007874 inches" },
        // ── Volumen — formas largas ───────────────────────────────────────────
        { "10 gal",         "10 gallon / 10 gallons -> 37.85412 L / 37.85412 litres"      },
        // ── Fuerza — formas largas ────────────────────────────────────────────
        { "10 newton",      "10 N / 10 newtons -> 2.248089431 lbf / 2.248089431 pound-forces" },
        { "10 newtons",     "10 N / 10 newtons -> 2.248089431 lbf / 2.248089431 pound-forces" },
    };

    [Theory]
    [MemberData(nameof(UnitAliasCases))]
    public void UnitAlias_NormalizesToCanonical(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }
}

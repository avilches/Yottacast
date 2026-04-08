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
        { "10C",      "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10ºc",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10ºC",     "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10f",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        { "10ºf",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                      },
        { "10ºF",      "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                      },
        { "10 degc",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 DEGC",  "10 °C / 10 celsius -> 50 °F / 50 fahrenheit"                               },
        { "10 degf",  "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        { "10 DEGF",  "10 °F / 10 fahrenheit -> -12.22 °C / -12.22 celsius"                       },
        // ── Electricidad ────────────────────────────────────────────────────
        { "10w",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10W",      "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10watts",  "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        { "10Watts",  "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                               },
        // ── Tiempo ──────────────────────────────────────────────────────────
        { "10ms",          "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10millisecond", "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10milliseconds","10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10000 ms",      "10000 ms / 10000 milliseconds -> 10 s / 10 seconds"                        },
        // forceAmbiguous: mS (millisiemens) y MS (megasiemens) se redirigen a ms (milliseconds)
        { "10 mS",         "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10 MS",         "10 ms / 10 milliseconds -> 0.01 s / 0.01 seconds"                          },
        { "10h",           "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10H",           "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hour",        "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10Hour",        "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10hours",       "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10Hours",       "10 h / 10 hours -> 600 min / 600 minutes"                                  },
        { "10 d",          "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 D",          "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 day",        "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 days",       "10 day / 10 days -> 240 h / 240 hours"                                     },
        { "10 min",        "10 min / 10 minutes -> 600 s / 600 seconds"                                },
        { "10s",           "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10second",      "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10seconds",     "10 s / 10 seconds -> 10000 ms / 10000 milliseconds"                        },
        { "10Ms",          "10 Ms / 10 megaseconds -> 115 day 17 h 46 min 40 s / 115 days 17 hours 46 minutes 40 seconds" },
        // ── Normalize: descomposición en múltiples unidades ──────────────────
        { "38000s",        "38000 s / 38000 seconds -> 10 h 33 min 20 s / 10 hours 33 minutes 20 seconds" },
        { "48h",           "48 h / 48 hours -> 2 day / 2 days"                                             },
        { "49h",           "49 h / 49 hours -> 2 day 1 h / 2 days 1 hour"                                 },
        { "2500ms",        "2500 ms / 2500 milliseconds -> 2 s 500 ms / 2 seconds 500 milliseconds"        },
        { "10megasecond",  "10 Ms / 10 megaseconds -> 115 day 17 h 46 min 40 s / 115 days 17 hours 46 minutes 40 seconds" },
        { "10megaseconds", "10 Ms / 10 megaseconds -> 115 day 17 h 46 min 40 s / 115 days 17 hours 46 minutes 40 seconds" },
        // ── defaultPairs: fallback dimensional para prefijos exóticos no en defaultTargets ────────────
        // Unidades canónicas (kg, m…) usan defaultTargets. Las variantes con prefijo exótico
        // caen a defaultPairs: findDefaultTarget devuelve pair[0] (base SI) para cualquier unidad
        // dimensionalmente compatible que no sea exactamente pair[0] ni pair[1].
        { "10 Mm",    "10 Mm / 10 megameters -> 1e+7 m / 1e+7 meters"                             },
        { "10 Gg",    "10 Gg / 10 gigagrams -> 1e+7 kg / 1e+7 kilograms"                          },
        { "10 Gt",    "10 Gt / 10 gigatonnes -> 1e+13 kg / 1e+13 kilograms"                       },
        // ── Masa ────────────────────────────────────────────────────────────
        { "10t",      "10 t / 10 tonnes -> 22046.23 lb / 22046.23 pounds"                          },
        { "10tonnes", "10 t / 10 tonnes -> 22046.23 lb / 22046.23 pounds"                          },
        { "10 kg",    "10 kg / 10 kilograms -> 22.05 lb / 22.05 pounds"                           },
        { "10 g",     "10 g / 10 grams -> 0.353 oz / 0.353 ounces"                 },
        { "10 grams", "10 g / 10 grams -> 0.353 oz / 0.353 ounces"                 },
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
        { "10 mm",          "10 mm / 10 millimeters -> 0.394 in / 0.394 inches"          },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.394 in / 0.394 inches"          },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.394 in / 0.394 inches"          },
        { "10 ft",          "10 ft / 10 feet -> 3.05 m / 3.05 meters"                                  },
        { "10 feet",        "10 ft / 10 feet -> 3.05 m / 3.05 meters"                                  },
        { "1 foot",         "1 ft / 1 foot -> 0.305 m / 0.305 meters"                                },
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
        { "10 Pa",         "10 Pa / 10 pascals -> 0.00145 psi"                              },
        { "10 pascals",    "10 Pa / 10 pascals -> 0.00145 psi"                              },
        { "10 bar",        "10 bar / 10 bars -> 145.04 psi"                                         },
        { "10 atm",        "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 atmosphere", "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 atmospheres","10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"                     },
        { "10 psi",   "10 psi -> 0.689 bar / 0.689 bars"                           },
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
        { "10 kW",          "10 kW / 10 kilowatts -> 13.41 hp / 13.41 horsepowers"                    },
        { "1 kW",           "1 kW / 1 kilowatt -> 1.34 hp / 1.34 horsepowers"                         },
        { "0.01 kW",        "10 W / 10 watts -> 0.0134 hp / 0.0134 horsepowers"           },
        // ── Datos ───────────────────────────────────────────────────────────
        { "10 B",      "10 B / 10 bytes -> 0.01 kB / 0.01 kilobytes"                             },
        { "10000 B",   "10000 B / 10000 bytes -> 10 kB / 10 kilobytes"                          },
        { "10 kB",     "10 kB / 10 kilobytes -> 0.01 MB / 0.01 megabytes"                       },
        { "10000 kB",  "10000 kB / 10000 kilobytes -> 10 MB / 10 megabytes"                     },
        { "10 MB",     "10 MB / 10 megabytes -> 0.01 GB / 0.01 gigabytes"                       },
        { "10000 MB",  "10000 MB / 10000 megabytes -> 10 GB / 10 gigabytes"                     },
        { "10 GB",     "10 GB / 10 gigabytes -> 0.01 TB / 0.01 terabytes"                       },
        { "10000 GB",  "10000 GB / 10000 gigabytes -> 10 TB / 10 terabytes"                     },
        { "10 TB",     "10 TB / 10 terabytes -> 10000 GB / 10000 gigabytes"                     },
        // Normalize datos
        { "1500 MB",  "1500 MB / 1500 megabytes -> 1.5 GB / 1.5 gigabytes"                    },
        // ── Tiempo adicional ────────────────────────────────────────────────
        { "10 week",   "10 week / 10 weeks -> 70 day / 70 days"                                  },
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
        { "10 deg",      "10 deg / 10 degrees -> 0.175 rad / 0.175 radians"           },
        { "10 degree",   "10 deg / 10 degrees -> 0.175 rad / 0.175 radians"           },
        { "10 degrees",  "10 deg / 10 degrees -> 0.175 rad / 0.175 radians"           },
        { "10 grad",     "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 gradian",  "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 gradians", "10 grad / 10 gradians -> 9 deg / 9 degrees"                               },
        { "10 arcmin",   "10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                 },
        { "10 arcminute","10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                 },
        { "10 arcminutes","10 arcmin / 10 arcminutes -> 600 arcsec / 600 arcseconds"                },
        { "10 arcsec",   "10 arcsec / 10 arcseconds -> 0.167 arcmin / 0.167 arcminutes" },
        // ── Área ────────────────────────────────────────────────────────────
        { "10 m2",       "10 m2 -> 107.64 sqft"                                                     },
        { "10 sqft",     "10 sqft -> 0.929 m2"                                                  },
        { "10 sqin",     "10 sqin -> 0.0694 sqft"                                            },
        { "10 sqyd",     "10 sqyd -> 8.36 m2"                                                       },
        { "10 sqmi",     "10 sqmi -> 25.9 km2"                                                      },
        { "10 ha",       "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 hectare",  "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 hectares", "10 ha / 10 hectares -> 24.71 acre / 24.71 acres"                          },
        { "10 acre",     "10 acre / 10 acres -> 4.05 ha / 4.05 hectares"                            },
        { "10 acres",    "10 acre / 10 acres -> 4.05 ha / 4.05 hectares"                            },
        // ── Velocidad compuesta (compound unit_entry) ────────────────────────
        { "10 km/h",     "10 km / h / 10 kilometers per hour -> 6.21 mi / h / 6.21 miles per hour"   },
        { "60 mi/h",     "60 mi / h / 60 miles per hour -> 96.56 km / h / 96.56 kilometers per hour" },
        { "10 m/s",      "10 m / s / 10 meters per second -> 36 km / h / 36 kilometers per hour"     },
        // ── RPM ↔ Hz, y Hz prefijados ────────────────────────────────────────
        { "3000 rpm",    "3000 rpm / 3000 revolutions per minute -> 50 Hz / 50 hertz"                },
        { "50 Hz",       "50 Hz / 50 hertz -> 3000 rpm / 3000 revolutions per minute"                },
        { "10 hz",       "10 Hz / 10 hertz -> 600 rpm / 600 revolutions per minute"                  },  // alias lowercase
        // Prefijos Hz: cadena kHz→Hz→rpm, MHz→kHz, GHz→MHz, THz→GHz
        { "10 kHz",      "10 kHz / 10 kilohertz -> 10000 Hz / 10000 hertz"                           },
        { "10 MHz",      "10 MHz / 10 megahertz -> 10000 kHz / 10000 kilohertz"                      },
        { "10 GHz",      "10 GHz / 10 gigahertz -> 10000 MHz / 10000 megahertz"                      },
        { "10 THz",      "10 THz / 10 terahertz -> 10000 GHz / 10000 gigahertz"                      },
        { "10 Thz",      "10 THz / 10 terahertz -> 10000 GHz / 10000 gigahertz"                      },  // casing alternativo: Thz → THz
        // ── mph / kmh aliases ────────────────────────────────────────────────
        { "60 mph",      "60 mph / 60 miles per hour -> 96.56 kmh / 96.56 kilometers per hour"       },
        { "100 kmh",     "100 kmh / 100 kilometers per hour -> 62.14 mph / 62.14 miles per hour"     },
        { "100 kmph",    "100 kmh / 100 kilometers per hour -> 62.14 mph / 62.14 miles per hour"     },
        // ── Velocidad compuesta normalizada (unidades no estándar) ──────────────
        // Unidades con entrada directa en defaultTargets — FROM queda como el usuario escribió
        { "2 mi/s",          "2 mi / s / 2 miles per second -> 3.22 km / s / 3.22 kilometers per second"                    },
        { "60 mi/min",       "60 mi / minute / 60 miles per minute -> 96.56 km / minute / 96.56 kilometers per minute"      },
        { "5 ft/s",          "5 ft / s / 5 feet per second -> 1.52 m / s / 1.52 meters per second"                          },
        { "5 ft/min",        "5 ft / minute / 5 feet per minute -> 1.52 m / minute / 1.52 meters per minute"                },
        { "100 km/min",      "100 km / minute / 100 kilometers per minute -> 3728.23 mi / h / 3728.23 miles per hour"       },
        // Unidades no estándar — FROM queda tal como lo escribió el usuario, TO a mi/h vía par dimensional
        { "2000000 mm/min",  "2e+6 mm / minute / 2e+6 millimeters per minute -> 74.56 mi / h / 74.56 miles per hour"          },
        { "10 mm/s",         "10 mm / s / 10 millimeters per second -> 0.0224 mi / h / 0.0224 miles per hour"                 },
        { "50 cm/s",         "50 cm / s / 50 centimeters per second -> 1.12 mi / h / 1.12 miles per hour"                     },
        { "10 Mm/min",       "10 Mm / minute / 10 megameters per minute -> 3.728227153e+5 mi / h / 3.728227153e+5 miles per hour" },
        // ── Tasas de datos (bit/s ↔ byte/s) ─────────────────────────────────────
        { "1 Gbps",      "1 Gbps / 1 gigabit per second -> 125 MB / s / 125 megabytes per second"    },
        { "100 Mbps",    "100 Mbps / 100 megabits per second -> 12.5 MB / s / 12.5 megabytes per second" },
        { "10 kbps",     "10 kbps / 10 kilobits per second -> 1.25 kB / s / 1.25 kilobytes per second" },
        { "10 kB/s",     "10 kB / s / 10 kilobytes per second -> 0.08 Mbps / 0.08 megabits per second" },
        { "100 MB/s",    "100 MB / s / 100 megabytes per second -> 800 Mbps / 800 megabits per second" },
        { "10 GB/s",     "10 GB / s / 10 gigabytes per second -> 80 Gbps / 80 gigabits per second"  },
        // ── Números grandes (normalizeUnits): from preservado en notación científica ─
        // math.js usa notación científica para |x| >= 1e5 (e.g. 1e6 → "1e+6").
        // El from queda en la unidad original; TryNormalize descompone el to.
        { "1000000 s",  "1e+6 s / 1e+6 seconds -> 11 day 13 h 46 min 40 s / 11 days 13 hours 46 minutes 40 seconds" },
        { "1000000 ms", "1e+6 ms / 1e+6 milliseconds -> 16 min 40 s / 16 minutes 40 seconds"                        },
        { "1000000 B",  "1e+6 B / 1e+6 bytes -> 1 MB / 1 megabyte"                                                  },
        { "1000000 kB", "1e+6 kB / 1e+6 kilobytes -> 1 GB / 1 gigabyte"                                             },
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
        { "10 minutes",     "10 min / 10 minutes " +
                            "-> 600 s / 600 seconds"                },
        { "10 days",        "10 day / 10 days -> 240 h / 240 hours"                     },
        { "10 weeks",       "10 week / 10 weeks -> 70 day / 70 days"                    },
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
        { "10 gram",        "10 g / 10 grams -> 0.353 oz / 0.353 ounces"    },
        { "10 tonne",       "10 t / 10 tonnes -> 22046.23 lb / 22046.23 pounds"            },
        // ── Electricidad — formas largas ──────────────────────────────────────
        { "10 watt",        "10 W / 10 watts -> 0.01 kW / 0.01 kilowatts"                },
        // ── Presión — formas largas ───────────────────────────────────────────
        { "10 pascal",      "10 Pa / 10 pascals -> 0.00145 psi"                    },
        { "10 atmosphere",  "10 atm / 10 atmospheres -> 10.13 bar / 10.13 bars"           },
        // ── Longitud — formas largas ──────────────────────────────────────────
        { "10 meter",       "10 m / 10 meters -> 32.81 ft / 32.81 feet"                   },
        { "10 meters",      "10 m / 10 meters -> 32.81 ft / 32.81 feet"                   },
        { "10 kilometer",   "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"             },
        { "10 kilometers",  "10 km / 10 kilometers -> 6.21 mile / 6.21 miles"             },
        { "10 centimeter",  "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"             },
        { "10 centimeters", "10 cm / 10 centimeters -> 3.94 in / 3.94 inches"             },
        { "10 millimeter",  "10 mm / 10 millimeters -> 0.394 in / 0.394 inches" },
        { "10 millimeters", "10 mm / 10 millimeters -> 0.394 in / 0.394 inches" },
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
        { "10 terabyte",    "10 TB / 10 terabytes -> 10000 GB / 10000 gigabytes"          },
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
        { "1 horsepower",  "1 hp / 1 horsepower -> 0.746 kW / 0.746 kilowatts" },
        { "1 horsepowers", "1 hp / 1 horsepower -> 0.746 kW / 0.746 kilowatts" },
        // ── Smoke test 0.01 y 0.00001 — escala SI y comportamiento de normalizeUnits ─
        // Unidades SI estándar: math.js auto-simplifica cuando el coeficiente cae fuera
        //   del rango [0.1, 1000]:
        //   coeff < 0.1 → simplifica hacia abajo (0.01 km → 10 m; 0.01 mm → 10 µm)
        //   coeff > 1000 → simplifica hacia arriba (2000 m → 2 km; 1000000 m → 1 Mm)
        //   coeff ∈ [0.1, 1000] → sin cambio (0.1 m → 0.1 m; 1000 m → 1000 m)
        // normalizeUnits (tiempo, datos): TryNormalize fuerza "to origUnit" para fijar el from.
        //   Si el resultado difiere de la unidad origen, el from queda PRESERVADO (0.01 s → from "0.01 s").
        //   Si TryNormalize falla (e.g. 0.01 ms → cadena devuelve misma unidad ms, isInteresting=false),
        //   cae al path regular y math.js sí auto-simplifica (0.01 ms → from "10 µs").
        // ── Temperatura ───────────────────────────────────────────────────────
        { "0.01c",      "10 mdegC / 10 millicelsius -> 32.02 °F / 32.02 fahrenheit"      },
        { "0.01f",      "10 mdegF / 10 millifahrenheit -> -17.77 °C / -17.77 celsius"    },
        // La parte decimal es tan pequeña que se absorbe en el redondeo a 2 decimales → "32"
        { "0.00001c",   "10 udegC / 10 microcelsius -> 32 °F / 32 fahrenheit"            },
        // 0.00001 = 1e-5: dos pasos SI de simplificación (m→mm→µm; g→mg→µg)
        { "0.00001 m",  "10 um / 10 micrometers -> 3.280839895e-5 ft / 3.280839895e-5 feet"     },
        { "0.00001 g",  "10 ug / 10 micrograms -> 3.527396195e-7 oz / 3.527396195e-7 ounces"    },
        // normalizeUnits (s): from preservado en notación científica; 1e-5 s = 0.01 ms
        { "0.00001 s",  "1e-5 s / 1e-5 seconds -> 0.01 ms / 0.01 milliseconds"                  },
        // ── Electricidad ──────────────────────────────────────────────────────
        { "0.01W",      "10 mW / 10 milliwatts -> 1e-5 kW / 1e-5 kilowatts"             },
        // C y F son aliases de degC/degF
        { "0.01C",      "10 mdegC / 10 millicelsius -> 32.02 °F / 32.02 fahrenheit"     },
        { "0.01F",      "10 mdegF / 10 millifahrenheit -> -17.77 °C / -17.77 celsius"   },
        // ── Tiempo ────────────────────────────────────────────────────────────
        { "0.01h",      "0.01 h / 0.01 hours -> 36 s / 36 seconds"                        },
        { "0.01 day",   "0.01 day / 0.01 days -> 14 min 24 s / 14 minutes 24 seconds"    },
        { "0.01 min",   "0.01 min / 0.01 minutes -> 600 ms / 600 milliseconds"           },
        { "0.01s",      "0.01 s / 0.01 seconds -> 10 ms / 10 milliseconds"              },
        { "0.01ms",     "10 us / 10 microseconds -> 1e-5 s / 1e-5 seconds"               },
        { "0.001ms",    "1 us / 1 microsecond -> 1e-6 s / 1e-6 seconds"                },
        { "0.01Ms",     "0.01 Ms / 0.01 megaseconds -> 2 h 46 min 40 s / 2 hours 46 minutes 40 seconds" },
        // ── Masa ──────────────────────────────────────────────────────────────
        { "0.01t",      "10 mt / 10 millitonnes -> 22.05 lb / 22.05 pounds"              },
        { "0.01 g",     "10 mg / 10 milligrams -> 3.527396195e-4 oz / 3.527396195e-4 ounces" },
        { "0.01 oz",    "0.01 oz / 0.01 ounces -> 0.283 g / 0.283 grams"  },
        { "0.01 lb",    "0.01 lb / 0.01 pounds -> 0.00454 kg / 0.00454 kilograms" },
        // ── Longitud ──────────────────────────────────────────────────────────
        { "0.01 m",     "10 mm / 10 millimeters -> 0.0328 ft / 0.0328 feet" },
        { "0.01 km",    "10 m / 10 meters -> 0.00621 mile / 0.00621 miles" },
        { "0.01 cm",    "100 um / 100 micrometers -> 0.00394 in / 0.00394 inches" },
        { "0.01 mm",    "10 um / 10 micrometers -> 3.937007874e-4 in / 3.937007874e-4 inches" },
        { "0.01 ft",    "0.01 ft / 0.01 feet -> 0.00305 m / 0.00305 meters"            },
        { "0.01 in",    "0.01 in / 0.01 inches -> 0.0254 cm / 0.0254 centimeters"        },
        { "0.01 yard",  "0.01 yard / 0.01 yards -> 0.00914 m / 0.00914 meters"         },
        { "0.01 mi",    "0.01 mi / 0.01 miles -> 0.0161 km / 0.0161 kilometers"  },
        // ── Volumen ───────────────────────────────────────────────────────────
        { "0.01 l",     "10 mL / 10 millilitres -> 0.00264 gallon / 0.00264 gallons" },
        { "0.01 gal",   "0.01 gallon / 0.01 gallons -> 0.0379 L / 0.0379 litres" },
        // ── Presión ───────────────────────────────────────────────────────────
        { "0.01 Pa",    "10 mPa -> 1.450377377e-6 psi"                                   },
        { "0.01 bar",   "10 mbar -> 0.145 psi"                                    },
        { "0.01 atm",   "0.01 atm / 0.01 atmospheres -> 0.0101 bar / 0.0101 bars"  },
        { "0.01 psi",   "0.01 psi -> 6.894757293e-4 bar / 6.894757293e-4 bars"           },
        { "0.01 torr",  "0.01 torr -> 0.01 mmHg"                                         },
        { "0.01 mmHg",  "0.01 mmHg -> 0.00133 kPa"                                   },
        // ── Fuerza ────────────────────────────────────────────────────────────
        { "0.01 N",     "10 mN / 10 millinewtons -> 0.00225 lbf / 0.00225 pound-forces" },
        { "0.01 lbf",   "0.01 lbf / 0.01 pound-forces -> 0.0445 N / 0.0445 newtons" },
        { "0.01 kgf",   "0.01 kgf / 0.01 kilogram-forces -> 0.0981 N / 0.0981 newtons" },
        { "0.01 dyn",   "10 mdyn / 10 millidynes -> 1e-4 mN / 1e-4 millinewtons"        },
        // ── Energía ───────────────────────────────────────────────────────────
        { "0.01 J",     "10 mJ / 10 millijoules -> 1e-5 kJ / 1e-5 kilojoules"             },
        { "0.01 kJ",    "10 J / 10 joules -> 0.00278 Wh"                                },
        { "0.01 Wh",    "10 mWh -> 0.036 kJ / 0.036 kilojoules"                         },
        { "0.01 eV",    "10 meV / 10 millielectronvolts -> 1.602176565e-21 J / 1.602176565e-21 joules" },
        { "0.01 erg",   "10 merg -> 1e-9 J / 1e-9 joules"                               },
        // ── Potencia ──────────────────────────────────────────────────────────
        { "0.01 hp",    "0.01 hp / 0.01 horsepowers -> 0.00746 kW / 0.00746 kilowatts" },
        // ── Datos ─────────────────────────────────────────────────────────────
        { "0.01 B",     "0.01 B / 0.01 bytes -> 1e-5 kB / 1e-5 kilobytes"                 },
        { "0.01 kB",    "0.01 kB / 0.01 kilobytes -> 10 B / 10 bytes"                    },
        { "0.01 MB",    "0.01 MB / 0.01 megabytes -> 10 kB / 10 kilobytes"              },
        { "0.01 GB",    "0.01 GB / 0.01 gigabytes -> 10 MB / 10 megabytes"              },
        { "0.01 TB",    "0.01 TB / 0.01 terabytes -> 10 GB / 10 gigabytes"              },
        // ── Tiempo adicional ──────────────────────────────────────────────────
        { "0.01 year",   "0.01 year / 0.01 years -> 3 day 15 h 39 min 36 s / 3 days 15 hours 39 minutes 36 seconds" },
        { "0.01 decade", "0.01 decade / 0.01 decades -> 0.1 year / 0.1 years"           },
        // ── Volumen menor ─────────────────────────────────────────────────────
        { "0.01 pint",   "0.01 pint / 0.01 pints -> 0.02 cup / 0.02 cups"    },
        { "0.01 quart",  "0.01 quart / 0.01 quarts -> 0.02 pint / 0.02 pints" },
        { "0.01 cup",    "0.01 cup / 0.01 cups -> 0.08 floz / 0.08 fluid ounces" },
        { "0.01 floz",   "0.01 floz / 0.01 fluid ounces -> 0.296 mL / 0.296 millilitres" },
        { "0.01 tbsp",   "0.01 tablespoon / 0.01 tablespoons -> 0.03 teaspoon / 0.03 teaspoons" },
        { "0.01 tsp",    "0.01 teaspoon / 0.01 teaspoons -> 0.05 mL / 0.05 millilitres" },
        { "0.01 cc",     "0.01 cc / 0.01 cubic centimeters -> 0.01 mL / 0.01 millilitres" },
        // ── Ángulo ────────────────────────────────────────────────────────────
        { "0.01 rad",   "10 mrad / 10 milliradians -> 0.573 deg / 0.573 degrees" },
        { "0.01 deg",   "10 mdeg / 10 millidegrees -> 1.745329252e-4 rad / 1.745329252e-4 radians" },
        { "0.01 grad",  "10 mgrad / 10 milligradians -> 0.009 deg / 0.009 degrees"       },
        { "0.01 arcmin","0.01 arcmin / 0.01 arcminutes -> 0.6 arcsec / 0.6 arcseconds"  },
        // ── Área ──────────────────────────────────────────────────────────────
        { "0.01 m2",    "10000 mm2 -> 0.108 sqft"                                 },
        { "0.01 sqft",  "0.01 sqft -> 9.290304e-4 m2"                                    },
        { "0.01 ha",    "0.01 ha / 0.01 hectares -> 0.0247 acre / 0.0247 acres" },
        { "0.01 acre",  "0.01 acre / 0.01 acres -> 0.00405 ha / 0.00405 hectares"  },
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
    // más conveniente. La simplificación ocurre en AMBAS direcciones:
    //   Hacia ABAJO cuando coeff < 0.1: 0.001 m → 1 mm, 0.00001 m → 10 µm.
    //   Para valores "medios" (coeff ∈ [0.1, 1000]): sin cambio (0.1 m → 0.1 m; 1000 m → 1000 m).
    //   Hacia ARRIBA cuando coeff > 1000: 2000 m → 2 km, 1000000 m → 1 Mm.
    // Las unidades imperiales y no-SI (oz, ft, atm, psi, hp, acre…) NO se
    // normalizan nunca — conservan el valor tal como lo escribió el usuario.
    // EXCEPCIÓN — normalizeUnits (tiempo, datos): TryNormalize fuerza la evaluación
    // "... to origUnit", fijando la unidad origen y preservando el from SIEMPRE QUE
    // computeNormalization encuentre una descomposición con unidad distinta (isInteresting).
    //   0.001 s → from "0.001 s" (TryNormalize éxito: to "1 ms", isInteresting=true)
    //   0.01 ms → from "10 µs"   (TryNormalize falla: computeNorm devuelve misma
    //                              unidad ms, isInteresting=false → fallthrough → math.js simplifica)
    // Ref: math.format() → unit.simplify() en math.js; TryNormalize en MathJsEngine.cs.

    public static TheoryData<string, string, string> FromPrefixNormalizationCases => new() {
        // query              from esperado               long suffix (o "" si null)
        // ── SI estándar (un paso): normaliza hacia abajo cuando coeff < 0.1 ───
        { "0.001 m",    "1 mm",         "millimeter"     },
        { "0.001 g",    "1 mg",         "milligram"      },
        { "0.001 J",    "1 mJ",         "millijoule"     },
        { "0.001 W",    "1 mW",         "milliwatt"      },
        { "0.001 N",    "1 mN",         "millinewton"    },
        { "0.001 Pa",   "1 mPa",        ""               },  // Pa no tiene longName en LONG prefix group
        // ── SI prefijado (k*): simplifica kX → X base cuando coeff < 0.1 ────
        { "0.001 km",   "1 m",          "meter"          },  // 0.001 × 1000 m = 1 m
        { "0.001 kg",   "1 g",          "gram"           },  // 0.001 × 1000 g = 1 g
        { "0.001 kJ",   "1 J",          "joule"          },  // 0.001 kJ = 1 J
        { "0.001 kW",   "1 W",          "watt"           },  // 0.001 kW = 1 W
        // ── SI (doble paso): 0.00001 = 1e-5 → dos niveles de simplificación ──
        { "0.00001 m",  "10 um",        "micrometers"    },  // 1e-5 m → µm (valor=10 → plural)
        { "0.00001 g",  "10 ug",        "micrograms"     },
        { "0.00001 W",  "10 uW",        "microwatts"     },
        { "0.00001 N",  "10 uN",        "micronewtons"   },
        // ── Frontera inferior exacta del umbral: 0.1 no simplifica, 0.09 sí ──
        { "0.1 m",      "0.1 m",        "meters"         },  // coeff=0.1 → en el límite, sin cambio
        { "0.09 m",     "90 mm",        "millimeters"    },  // coeff=0.09 < 0.1 → simplifica (valor=90 → plural)
        // ── SI: no simplifica en el rango "medio" (coeff ∈ [0.1, 1000]) ───────
        { "1000 m",     "1000 m",       "meters"         },  // coeff=1000 → en el límite, sin cambio
        { "1000 g",     "1000 g",       "grams"          },
        { "1000 W",     "1000 W",       "watts"          },
        // ── Frontera superior exacta del umbral: 1000 no simplifica, 1100 sí ─
        { "1100 m",     "1.1 km",       "kilometers"     },  // coeff=1100 > 1000 → simplifica (valor=1.1 → plural)
        { "2000 m",     "2 km",         "kilometers"     },  // coeff=2000 > 1000 → sube a km (valor=2 → plural)
        // ── SI (muy grande): simplificación upward hasta prefijo mega ─────────
        // Aplica a TODAS las familias SI — no solo m/g/W
        { "1000000 m",  "1 Mm",         "megameter"      },  // longitud
        { "1000000 g",  "1 Mg",         "megagram"       },  // masa
        { "1000000 W",  "1 MW",         "megawatt"       },  // potencia
        { "1000000 J",  "1 MJ",         "megajoule"      },  // energía
        { "1000000 N",  "1 MN",         "meganewton"     },  // fuerza
        // ── Cascade T→P: 10000 × cualquier unidad T = 10 × esa unidad en P ──
        // Documenta que la regla [0.1, 1000] aplica en TODOS los niveles de prefijo
        { "10000 TW",   "10 PW",        "petawatts"      },  // potencia
        { "10000 TJ",   "10 PJ",        "petajoules"     },  // energía
        { "10000 TN",   "10 PN",        "petanewtons"    },  // fuerza
        { "10000 THz",  "10 PHz",       "petahertz"      },  // frecuencia (hertz no pluraliza)
        // ── L (litro): LONG prefix en math.js → usa ortografía "litre" (británico) ─
        { "0.001 L",    "1 mL",         "millilitre"     },  // downward (valor=1 → singular)
        { "1000000 L",  "1 ML",         "megalitre"      },  // upward (valor=1 → singular)
        { "10000 TL",   "10 PL",        "petalitres"     },  // T→P cascade (valor=10 → plural)
        // ── rad (radián): LONG prefix en math.js → nombre largo derivable ──
        { "0.001 rad",   "1 mrad",      "milliradian"    },  // downward (valor=1 → singular)
        { "1000000 rad", "1 Mrad",      "megaradian"     },  // upward (valor=1 → singular)
        { "10000 Trad",  "10 Prad",     "petaradians"    },  // T→P cascade (valor=10 → plural)
        // ── normalizeUnits: TryNormalize preserva el from cuando isInteresting ─
        { "0.001 s",    "0.001 s",      "seconds"        },  // to: 1 ms
        // ── normalizeUnits (ms): TryNormalize falla → fallthrough → math.js simplifica ─
        { "0.01 ms",    "10 us",        "microseconds"   },  // isInteresting=false → 10 µs (valor=10 → plural)
        { "0.001 ms",   "1 us",         "microsecond"    },  // valor=1 → singular
        // ── normalizeUnits (datos): bytes no se simplifican hacia abajo ────────
        { "0.01 B",     "0.01 B",       "bytes"          },  // TryNormalize falla; math.js no simplifica B (valor≠1 → plural)
        // ── normalizeUnits (grande): TryNormalize fuerza "to origUnit" → from preservado ─
        // (sin upward simplification aunque coeff sea 1e6, porque EvalJs fija la unidad)
        { "1000000 s",  "1e+6 s",       "seconds"        },  // → 11 day 13 h 46 min 40 s
        { "1000000 ms", "1e+6 ms",      "milliseconds"   },  // → 16 min 40 s
        { "1000000 B",  "1e+6 B",       "bytes"          },  // → 1 MB
        // ── No-SI / imperial: nunca normaliza en ninguna dirección ──────────
        // Hacia abajo: valores pequeños se conservan tal como los escribió el usuario
        { "0.001 ft",   "0.001 ft",     "feet"           },
        { "0.001 oz",   "0.001 oz",     "ounces"         },
        { "0.001 atm",  "0.001 atm",    "atmospheres"    },
        { "0.001 psi",  "0.001 psi",    ""               },  // psi no tiene longName
        { "0.001 hp",   "0.001 hp",     "horsepowers"    },
        { "0.001 acre", "0.001 acre",   "acres"          },
        // Hacia arriba: valores muy grandes tampoco se simplican (a diferencia de SI)
        // math.js formatea el número en notación científica pero NO cambia la unidad
        { "1000000 ft", "1e+6 ft",      "feet"           },  // SI daría 1 Mft; imperial queda en ft
        { "1000000 oz", "1e+6 oz",      "ounces"         },
    };

    // ── Velocidad compuesta — nuevas unidades ────────────────────────────────
    public static TheoryData<string, string> NewCompoundUnitCases => new() {
        // yard/h, ft/h — en defaultTargets, FROM queda como el usuario escribió
        { "10 yard/h",  "10 yard / h / 10 yards per hour -> 0.00914 km / h / 0.00914 kilometers per hour" },
        { "5 ft/h",     "5 ft / h / 5 feet per hour -> 0.00152 km / h / 0.00152 kilometers per hour"      },
        // mm/h, cm/h — FROM queda tal como lo escribió el usuario, TO a mi/h vía par dimensional
        { "10 mm/h",    "10 mm / h / 10 millimeters per hour -> 6.213711922e-6 mi / h / 6.213711922e-6 miles per hour" },
        { "10 cm/h",    "10 cm / h / 10 centimeters per hour -> 6.213711922e-5 mi / h / 6.213711922e-5 miles per hour" },
    };

    [Theory]
    [MemberData(nameof(NewCompoundUnitCases))]
    public void NewCompoundUnit_DefaultConversion(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    // ── Conversión explícita de unidades compuestas — long names en TO ───────
    public static TheoryData<string, string> ExplicitCompoundConversionCases => new() {
        // FROM siempre en la unidad compuesta original (no auto-simplificada a custom unit)
        { "10 mi/ms to m/h",    "10 mi / ms / 10 miles per millisecond -> 5.7936384e+10 m / h / 5.7936384e+10 meters per hour" },
        { "10 yard/h to mi/s",  "10 yard / h / 10 yards per hour -> 1.578282828e-6 mi / s / 1.578282828e-6 miles per second" },
        { "10 mi/s to ft/s",    "10 mi / s / 10 miles per second -> 52800 ft / s / 52800 feet per second" },
    };

    [Theory]
    [MemberData(nameof(ExplicitCompoundConversionCases))]
    public void ExplicitCompoundConversion_ShowsLongNameInTo(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    // ── Prefijos SI en tiempo y datos ────────────────────────────────────────
    // Todos los prefijos SI de 's' y 'B' deben producir descomposición natural.
    // Los prefijos de tiempo menores que ms se expresan en ms (último paso de la cadena);
    // los mayores que s se descomponen en componentes naturales (días, horas, minutos...).
    // Los datos por encima de TB se expresan en TB porque la cadena llega hasta TB.

    public static TheoryData<string, string> SIPrefixedTimeCases => new() {
        // ── Prefijos sub-ms: resultado en ms (último escalón de la cadena) ────
        { "1 ys", "1 ys / 1 yoctosecond -> 1e-21 ms / 1e-21 milliseconds" },
        { "1 zs", "1 zs / 1 zeptosecond -> 1e-18 ms / 1e-18 milliseconds" },
        { "1 as", "1 as / 1 attosecond -> 1e-15 ms / 1e-15 milliseconds"  },
        { "1 fs", "1 fs / 1 femtosecond -> 1e-12 ms / 1e-12 milliseconds" },
        { "1 ps", "1 ps / 1 picosecond -> 1e-9 ms / 1e-9 milliseconds"    },
        { "1 ns", "1 ns / 1 nanosecond -> 1e-6 ms / 1e-6 milliseconds"    },
        { "1 us", "1 us / 1 microsecond -> 0.001 ms / 0.001 milliseconds" },
        // ── Prefijos supra-s: descomposición natural ─────────────────────────
        { "1 ks", "1 ks / 1 kilosecond -> 16 min 40 s / 16 minutes 40 seconds"                                                    },
        { "1 Ms", "1 Ms / 1 megasecond -> 11 day 13 h 46 min 40 s / 11 days 13 hours 46 minutes 40 seconds"                      },
        { "1 Gs", "1 Gs / 1 gigasecond -> 31 year 251 day 7 h 46.67 min / 31 years 251 days 7 hours 46.67 minutes"               },
        { "1 Ts", "1 Ts / 1 terasecond -> 31688 year 32 day 1 h 46.67 min / 31688 years 32 days 1 hour 46.67 minutes"           },
        { "1 Ps", "1 Ps / 1 petasecond -> 31688087 year 297 day / 31688087 years 297 days"                                      },
        { "1 Es", "1 Es / 1 exasecond -> 31688087814 year / 31688087814 years"                                                   },
        { "1 Zs", "1 Zs / 1 zettasecond -> 31688087814028 year / 31688087814028 years"                                          },
        { "1 Ys", "1 Ys / 1 yottasecond -> 31688087814028948 year / 31688087814028948 years"                                    },
    };

    [Theory]
    [MemberData(nameof(SIPrefixedTimeCases))]
    public void SIPrefixed_TimeUnits_Decompose(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

    public static TheoryData<string, string> SIPrefixedDataCases => new() {
        // ── PB, EB, ZB, YB: la cadena best_unit llega hasta TB, se expresan en TB ──
        // fromLong es null porque math.js no puede derivar el nombre largo de estos símbolos
        { "1 PB", "1 PB -> 1000 TB / 1000 terabytes"          },
        { "1 EB", "1 EB -> 1000000 TB / 1000000 terabytes"     },
        { "1 ZB", "1 ZB -> 1000000000 TB / 1000000000 terabytes" },
        { "1 YB", "1 YB -> 1000000000000 TB / 1000000000000 terabytes" },
    };

    [Theory]
    [MemberData(nameof(SIPrefixedDataCases))]
    public void SIPrefixed_DataUnits_BestUnit(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(expectedSummary, summary);
    }

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

    [Fact]
    public void PROBE_GenerateSIPrefixData() {
        var units = new (string Unit, string[] Prefixes)[] {
            ("m",   ["y","z","a","f","p","n","u","m","c","d","k","M","G","T","P","E","Z","Y"]),
            ("g",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("W",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("J",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("N",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("L",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("rad", ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("Hz",  ["m","k","M","G","T","P","E","Z","Y"]),
            ("t",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("s",   ["y","z","a","f","p","n","u","m","k","M","G","T","P","E","Z","Y"]),
            ("B",   ["k","M","G","T","P","E","Z","Y"]),
        };
        var sb = new System.Text.StringBuilder();
        string Try(string q) {
            try {
                var item = GetConversionItem(q);
                return $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
            } catch (Exception ex) { return $"FAIL: {ex.Message.Split('\n')[0]}"; }
        }
        foreach (var (unit, prefixes) in units) {
            sb.AppendLine($"\n    // ── {unit} ──");
            sb.AppendLine($"    {{ \"1 {unit}\", \"{Try($"1 {unit}")}\" }},");
            foreach (var p in prefixes)
                sb.AppendLine($"    {{ \"1 {p}{unit}\", \"{Try($"1 {p}{unit}")}\" }},");
        }
        Assert.Fail(sb.ToString());
    }
}

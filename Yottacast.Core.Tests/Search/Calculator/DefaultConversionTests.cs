using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yottacast.Core.Search.Calculator;
using Yottacast.Core.Services;
using Yottacast.Core.Tests.Fakes;
using Yottacast.Core.ViewModels;

namespace Yottacast.Core.Tests.Search.Calculator;

// NOTE: Column alignment of TheoryData rows is enforced automatically by
// DefaultConversionTestsFormatting.cs — do not manually align "|" and "->".
// Just run `dotnet test` and the formatter will rewrite this file if needed.
[Collection("MathJs")]
public class DefaultConversionTests(MathJsEngineFixture fixture) {

    private ConversionResultItemViewModel GetConversionItem(string query) {
        var (item, _) = GetConversionItemWithSearch(query);
        return item;
    }

    private (ConversionResultItemViewModel Item, CalculatorSearch Search) GetConversionItemWithSearch(string query) {
        var clipboard = new ClipboardService(NullLogger<ClipboardService>.Instance);
        var settings = UserSettings.Load(new FakePlatformProvider([]));
        var search = new CalculatorSearch(fixture.Engine, clipboard, settings, NullLogger<CalculatorSearch>.Instance);
        var results = search.Search(query, 5);
        var item = Assert.Single(results);
        return (Assert.IsType<ConversionResultItemViewModel>(item), search);
    }

    // Formats both short and long forms: "10 km | 10 kilometers" or just "10 B" when long is null.
    private static string Fmt(string s, string? l) => l is null ? s : $"{s} | {l}";

    // Formats the full conversion summary including the norm-from cell when math.js changed the unit.
    // Without normalization: "10 h | 10 hours -> 600 min | 600 minutes"
    // With normalization:    "0.01 g | 0.01 grams > 10 mg | 10 milligrams -> 3.5e-4 oz | ..."
    private static string FmtFull(ConversionResultItemViewModel item) {
        var from = Fmt(item.FromShort, item.FromLong);
        var to   = Fmt(item.ToShort, item.ToLong);
        if (item.NormFromShort is not null) {
            var normFrom = Fmt(item.NormFromShort, item.NormFromLong);
            return $"{from} > {normFrom} -> {to}";
        }
        return $"{from} -> {to}";
    }

    // ── Casos de conversión por defecto ──────────────────────────────────────

    public static TheoryData<string, string> DefaultConversionCases => new() {
        // ── Temperatura ──────────────────────────────────────────────────────
        // aliases c/f vs. C/F mayúscula
        { "10c",            "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10C",            "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10ºc",           "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10ºC",           "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10f",            "10 °F          | 10 fahrenheit                                    -> -12.22 °C                | -12.22 celsius" },
        { "10ºf",           "10 °F          | 10 fahrenheit                                    -> -12.22 °C                | -12.22 celsius" },
        { "10ºF",           "10 °F          | 10 fahrenheit                                    -> -12.22 °C                | -12.22 celsius" },
        { "10 degc",        "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10 degC",        "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10 DEGC",        "10 °C          | 10 celsius                                       -> 50 °F                    | 50 fahrenheit" },
        { "10 degf",        "10 °F          | 10 fahrenheit                                    -> -12.22 °C                | -12.22 celsius" },
        { "10 DEGF",        "10 °F          | 10 fahrenheit                                    -> -12.22 °C                | -12.22 celsius" },
        // ── Electricidad ────────────────────────────────────────────────────
        { "10w",            "10 W           | 10 watts                                         -> 0.01 kW                  | 0.01 kilowatts" },
        { "10W",            "10 W           | 10 watts                                         -> 0.01 kW                  | 0.01 kilowatts" },
        { "10watts",        "10 W           | 10 watts                                         -> 0.01 kW                  | 0.01 kilowatts" },
        { "10Watts",        "10 W           | 10 watts                                         -> 0.01 kW                  | 0.01 kilowatts" },
        // ── Tiempo ──────────────────────────────────────────────────────────
        { "10ms",           "10 ms          | 10 milliseconds                                  -> 0.01 s                   | 0.01 seconds" },
        { "10millisecond",  "10 ms          | 10 milliseconds                                  -> 0.01 s                   | 0.01 seconds" },
        { "10milliseconds", "10 ms          | 10 milliseconds                                  -> 0.01 s                   | 0.01 seconds" },
        { "10000 ms",       "10000 ms       | 10000 milliseconds                               -> 10 s                     | 10 seconds" },
        // forceAmbiguous: mS (millisiemens) y MS (megasiemens) se redirigen a ms (milliseconds)
        { "10 mS",          "10 ms          | 10 milliseconds                                  -> 0.01 s                   | 0.01 seconds" },
        { "10 MS",          "10 ms          | 10 milliseconds                                  -> 0.01 s                   | 0.01 seconds" },
        { "10h",            "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10H",            "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10hour",         "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10Hour",         "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10hours",        "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10Hours",        "10 h           | 10 hours                                         -> 600 min                  | 600 minutes" },
        { "10 d",           "10 day         | 10 days                                          -> 240 h                    | 240 hours" },
        { "10 D",           "10 day         | 10 days                                          -> 240 h                    | 240 hours" },
        { "10 day",         "10 day         | 10 days                                          -> 240 h                    | 240 hours" },
        { "10 days",        "10 day         | 10 days                                          -> 240 h                    | 240 hours" },
        { "10 min",         "10 min         | 10 minutes                                       -> 600 s                    | 600 seconds" },
        { "10s",            "10 s           | 10 seconds                                       -> 10000 ms                 | 10000 milliseconds" },
        { "10second",       "10 s           | 10 seconds                                       -> 10000 ms                 | 10000 milliseconds" },
        { "10seconds",      "10 s           | 10 seconds                                       -> 10000 ms                 | 10000 milliseconds" },
        { "10Ms",           "10 Ms          | 10 megaseconds                                   -> 115 day 17 h 46 min 40 s | 115 days 17 hours 46 minutes 40 seconds" },
        // ── Normalize: descomposición en múltiples unidades ──────────────────
        { "38000s",         "38000 s        | 38000 seconds                                    -> 10 h 33 min 20 s         | 10 hours 33 minutes 20 seconds" },
        { "48h",            "48 h           | 48 hours                                         -> 2 day                    | 2 days" },
        { "49h",            "49 h           | 49 hours                                         -> 2 day 1 h                | 2 days 1 hour" },
        { "2500ms",         "2500 ms        | 2500 milliseconds                                -> 2 s 500 ms               | 2 seconds 500 milliseconds" },
        { "10megasecond",   "10 Ms          | 10 megaseconds                                   -> 115 day 17 h 46 min 40 s | 115 days 17 hours 46 minutes 40 seconds" },
        { "10megaseconds",  "10 Ms          | 10 megaseconds                                   -> 115 day 17 h 46 min 40 s | 115 days 17 hours 46 minutes 40 seconds" },
        // ── defaultPairs: fallback dimensional para prefijos exóticos no en defaultTargets ────────────
        // Unidades canónicas (kg, m…) usan defaultTargets. Las variantes con prefijo exótico
        // caen a defaultPairs: findDefaultTarget devuelve pair[0] (base SI) para cualquier unidad
        // dimensionalmente compatible que no sea exactamente pair[0] ni pair[1].
        { "10 Mm",          "10 Mm          | 10 megameters                                    -> 10000000 m               | 10000000 meters" },
        { "10 Gg",          "10 Gg          | 10 gigagrams                                     -> 10000000 kg              | 10000000 kilograms" },
        { "10 Gt",          "10 Gt          | 10 gigatonnes                                    -> 1e+13 kg                 | 1e+13 kilograms" },
        // ── Masa ────────────────────────────────────────────────────────────
        { "10t",            "10 t           | 10 tonnes                                        -> 22046.23 lb              | 22046.23 pounds" },
        { "10tonnes",       "10 t           | 10 tonnes                                        -> 22046.23 lb              | 22046.23 pounds" },
        { "10 kg",          "10 kg          | 10 kilograms                                     -> 22.05 lb                 | 22.05 pounds" },
        { "10 g",           "10 g           | 10 grams                                         -> 0.353 oz                 | 0.353 ounces" },
        { "10 grams",       "10 g           | 10 grams                                         -> 0.353 oz                 | 0.353 ounces" },
        { "10 oz",          "10 oz          | 10 ounces                                        -> 283.5 g                  | 283.5 grams" },
        { "10 ounces",      "10 oz          | 10 ounces                                        -> 283.5 g                  | 283.5 grams" },
        { "10 lb",          "10 lb          | 10 pounds                                        -> 4.54 kg                  | 4.54 kilograms" },
        { "10 lbs",         "10 lb          | 10 pounds                                        -> 4.54 kg                  | 4.54 kilograms" },
        { "10 pound",       "10 lb          | 10 pounds                                        -> 4.54 kg                  | 4.54 kilograms" },
        { "10 pounds",      "10 lb          | 10 pounds                                        -> 4.54 kg                  | 4.54 kilograms" },
        // ── Longitud ────────────────────────────────────────────────────────
        { "10 m",           "10 m           | 10 meters                                        -> 32.81 ft                 | 32.81 feet" },
        { "10 meter",       "10 m           | 10 meters                                        -> 32.81 ft                 | 32.81 feet" },
        { "10 meters",      "10 m           | 10 meters                                        -> 32.81 ft                 | 32.81 feet" },
        { "10 km",          "10 km          | 10 kilometers                                    -> 6.21 mile                | 6.21 miles" },
        { "10 kilometer",   "10 km          | 10 kilometers                                    -> 6.21 mile                | 6.21 miles" },
        { "10 kilometers",  "10 km          | 10 kilometers                                    -> 6.21 mile                | 6.21 miles" },
        { "10 cm",          "10 cm          | 10 centimeters                                   -> 3.94 in                  | 3.94 inches" },
        { "10 centimeter",  "10 cm          | 10 centimeters                                   -> 3.94 in                  | 3.94 inches" },
        { "10 centimeters", "10 cm          | 10 centimeters                                   -> 3.94 in                  | 3.94 inches" },
        { "10 mm",          "10 mm          | 10 millimeters                                   -> 0.394 in                 | 0.394 inches" },
        { "10 millimeter",  "10 mm          | 10 millimeters                                   -> 0.394 in                 | 0.394 inches" },
        { "10 millimeters", "10 mm          | 10 millimeters                                   -> 0.394 in                 | 0.394 inches" },
        { "10 ft",          "10 ft          | 10 feet                                          -> 3.05 m                   | 3.05 meters" },
        { "10 feet",        "10 ft          | 10 feet                                          -> 3.05 m                   | 3.05 meters" },
        { "1 foot",         "1 ft           | 1 foot                                           -> 0.305 m                  | 0.305 meters" },
        { "10 in",          "10 in          | 10 inches                                        -> 25.4 cm                  | 25.4 centimeters" },
        { "10 inch",        "10 in          | 10 inches                                        -> 25.4 cm                  | 25.4 centimeters" },
        { "10 inches",      "10 in          | 10 inches                                        -> 25.4 cm                  | 25.4 centimeters" },
        { "10 yard",        "10 yard        | 10 yards                                         -> 9.14 m                   | 9.14 meters" },
        { "10 yards",       "10 yard        | 10 yards                                         -> 9.14 m                   | 9.14 meters" },
        { "10 mi",          "10 mi          | 10 miles                                         -> 16.09 km                 | 16.09 kilometers" },
        { "10 mile",        "10 mi          | 10 miles                                         -> 16.09 km                 | 16.09 kilometers" },
        { "10 miles",       "10 mi          | 10 miles                                         -> 16.09 km                 | 16.09 kilometers" },
        // ── Volumen ─────────────────────────────────────────────────────────
        { "10 l",           "10 L           | 10 litres                                        -> 2.64 gallon              | 2.64 gallons" },
        { "10 L",           "10 L           | 10 litres                                        -> 2.64 gallon              | 2.64 gallons" },
        { "10 gal",         "10 gallon      | 10 gallons                                       -> 37.85 L                  | 37.85 litres" },
        { "10 gallon",      "10 gallon      | 10 gallons                                       -> 37.85 L                  | 37.85 litres" },
        { "10 gallons",     "10 gallon      | 10 gallons                                       -> 37.85 L                  | 37.85 litres" },
        // ── Presión ─────────────────────────────────────────────────────────
        { "10 Pa",          "10 Pa          | 10 pascals                                       -> 0.00145 psi" },
        { "10 pascals",     "10 Pa          | 10 pascals                                       -> 0.00145 psi" },
        { "10 bar",         "10 bar         | 10 bars                                          -> 145.04 psi" },
        { "10 atm",         "10 atm         | 10 atmospheres                                   -> 10.13 bar                | 10.13 bars" },
        { "10 atmosphere",  "10 atm         | 10 atmospheres                                   -> 10.13 bar                | 10.13 bars" },
        { "10 atmospheres", "10 atm         | 10 atmospheres                                   -> 10.13 bar                | 10.13 bars" },
        { "10 psi",         "10 psi                                                            -> 0.689 bar                | 0.689 bars" },
        { "10 torr",        "10 torr                                                           -> 10 mmHg" },
        { "10 mmHg",        "10 mmHg                                                           -> 1.33 kPa" },
        // ── Fuerza ──────────────────────────────────────────────────────────
        { "10 N",           "10 N           | 10 newtons                                       -> 2.25 lbf                 | 2.25 pound-forces" },
        { "10 newton",      "10 N           | 10 newtons                                       -> 2.25 lbf                 | 2.25 pound-forces" },
        { "10 newtons",     "10 N           | 10 newtons                                       -> 2.25 lbf                 | 2.25 pound-forces" },
        { "10 lbf",         "10 lbf         | 10 pound-forces                                  -> 44.48 N                  | 44.48 newtons" },
        { "10 kgf",         "10 kgf         | 10 kilogram-forces                               -> 98.07 N                  | 98.07 newtons" },
        { "10 dyn",         "10 dyn         | 10 dynes                                         -> 0.1 mN                   | 0.1 millinewtons" },
        // ── Energía ─────────────────────────────────────────────────────────
        { "10 J",           "10 J           | 10 joules                                        -> 0.01 kJ                  | 0.01 kilojoules" },
        { "10 kJ",          "10 kJ          | 10 kilojoules                                    -> 2.78 Wh" },
        { "10 Wh",          "10 Wh                                                             -> 36 kJ                    | 36 kilojoules" },
        { "10 eV",          "10 eV          | 10 electronvolts                                 -> 1.602176565e-18 J        | 1.602176565e-18 joules" },
        { "10 erg",         "10 erg                                                            -> 1e-6 J                   | 1e-6 joules" },
        // ── Potencia ────────────────────────────────────────────────────────
        { "10 hp",          "10 hp          | 10 horsepowers                                   -> 7.46 kW                  | 7.46 kilowatts" },
        { "10 horsepower",  "10 hp          | 10 horsepowers                                   -> 7.46 kW                  | 7.46 kilowatts" },
        { "10 horsepowers", "10 hp          | 10 horsepowers                                   -> 7.46 kW                  | 7.46 kilowatts" },
        { "10 kW",          "10 kW          | 10 kilowatts                                     -> 13.41 hp                 | 13.41 horsepowers" },
        { "1 kW",           "1 kW           | 1 kilowatt                                       -> 1.34 hp                  | 1.34 horsepowers" },
        { "0.01 kW",        "0.01 kW        | 0.01 kilowatts                 > 10 W | 10 watts -> 0.0134 hp                | 0.0134 horsepowers" },
        // ── Datos ───────────────────────────────────────────────────────────
        { "10 B",           "10 B           | 10 bytes                                         -> 0.01 kB                  | 0.01 kilobytes" },
        { "10000 B",        "10000 B        | 10000 bytes                                      -> 10 kB                    | 10 kilobytes" },
        { "10 kB",          "10 kB          | 10 kilobytes                                     -> 0.01 MB                  | 0.01 megabytes" },
        { "10000 kB",       "10000 kB       | 10000 kilobytes                                  -> 10 MB                    | 10 megabytes" },
        { "10 MB",          "10 MB          | 10 megabytes                                     -> 0.01 GB                  | 0.01 gigabytes" },
        { "10000 MB",       "10000 MB       | 10000 megabytes                                  -> 10 GB                    | 10 gigabytes" },
        { "10 GB",          "10 GB          | 10 gigabytes                                     -> 0.01 TB                  | 0.01 terabytes" },
        { "10000 GB",       "10000 GB       | 10000 gigabytes                                  -> 10 TB                    | 10 terabytes" },
        { "10 TB",          "10 TB          | 10 terabytes                                     -> 10000 GB                 | 10000 gigabytes" },
        // Normalize datos
        { "1500 MB",        "1500 MB        | 1500 megabytes                                   -> 1.5 GB                   | 1.5 gigabytes" },
        // ── Tiempo adicional ────────────────────────────────────────────────
        { "10 week",        "10 week        | 10 weeks                                         -> 70 day                   | 70 days" },
        { "10 year",        "10 year        | 10 years                                         -> 3652.5 day               | 3652.5 days" },
        { "10 decade",      "10 decade      | 10 decades                                       -> 100 year                 | 100 years" },
        // ── Volumen menor ───────────────────────────────────────────────────
        { "10 pint",        "10 pint        | 10 pints                                         -> 20 cup                   | 20 cups" },
        { "10 quart",       "10 quart       | 10 quarts                                        -> 20 pint                  | 20 pints" },
        { "10 cup",         "10 cup         | 10 cups                                          -> 80 floz                  | 80 fluid ounces" },
        { "10 floz",        "10 floz        | 10 fluid ounces                                  -> 295.74 mL                | 295.74 millilitres" },
        { "10 tbsp",        "10 tablespoon  | 10 tablespoons                                   -> 30 teaspoon              | 30 teaspoons" },
        { "10 tsp",         "10 teaspoon    | 10 teaspoons                                     -> 50 mL                    | 50 millilitres" },
        { "10 cc",          "10 cc          | 10 cubic centimeters                             -> 10 mL                    | 10 millilitres" },
        // ── Ángulo ──────────────────────────────────────────────────────────
        { "10 rad",         "10 rad         | 10 radians                                       -> 572.96 deg               | 572.96 degrees" },
        { "10 radian",      "10 rad         | 10 radians                                       -> 572.96 deg               | 572.96 degrees" },
        { "10 radians",     "10 rad         | 10 radians                                       -> 572.96 deg               | 572.96 degrees" },
        { "10 deg",         "10 deg         | 10 degrees                                       -> 0.175 rad                | 0.175 radians" },
        { "10 degree",      "10 deg         | 10 degrees                                       -> 0.175 rad                | 0.175 radians" },
        { "10 degrees",     "10 deg         | 10 degrees                                       -> 0.175 rad                | 0.175 radians" },
        { "10 grad",        "10 grad        | 10 gradians                                      -> 9 deg                    | 9 degrees" },
        { "10 gradian",     "10 grad        | 10 gradians                                      -> 9 deg                    | 9 degrees" },
        { "10 gradians",    "10 grad        | 10 gradians                                      -> 9 deg                    | 9 degrees" },
        { "10 arcmin",      "10 arcmin      | 10 arcminutes                                    -> 600 arcsec               | 600 arcseconds" },
        { "10 arcminute",   "10 arcmin      | 10 arcminutes                                    -> 600 arcsec               | 600 arcseconds" },
        { "10 arcminutes",  "10 arcmin      | 10 arcminutes                                    -> 600 arcsec               | 600 arcseconds" },
        { "10 arcsec",      "10 arcsec      | 10 arcseconds                                    -> 0.167 arcmin             | 0.167 arcminutes" },
        // ── Área ────────────────────────────────────────────────────────────
        { "10 m2",          "10 m2                                                             -> 107.64 sqft" },
        { "10 sqft",        "10 sqft                                                           -> 0.929 m2" },
        { "10 sqin",        "10 sqin                                                           -> 0.0694 sqft" },
        { "10 sqyd",        "10 sqyd                                                           -> 8.36 m2" },
        { "10 sqmi",        "10 sqmi                                                           -> 25.9 km2" },
        { "10 ha",          "10 ha          | 10 hectares                                      -> 24.71 acre               | 24.71 acres" },
        { "10 hectare",     "10 ha          | 10 hectares                                      -> 24.71 acre               | 24.71 acres" },
        { "10 hectares",    "10 ha          | 10 hectares                                      -> 24.71 acre               | 24.71 acres" },
        { "10 acre",        "10 acre        | 10 acres                                         -> 4.05 ha                  | 4.05 hectares" },
        { "10 acres",       "10 acre        | 10 acres                                         -> 4.05 ha                  | 4.05 hectares" },
        // ── Velocidad compuesta (compound unit_entry) ────────────────────────
        { "1 km/h",         "1 km/h         | 1 kilometer per hour                             -> 0.621 mi/h               | 0.621 miles per hour" },
        { "10 km/h",        "10 km/h        | 10 kilometers per hour                           -> 6.21 mi/h                | 6.21 miles per hour" },
        { "1 mi/h",         "1 mi/h         | 1 mile per hour                                  -> 1.61 km/h                | 1.61 kilometers per hour" },
        { "60 mi/h",        "60 mi/h        | 60 miles per hour                                -> 96.56 km/h               | 96.56 kilometers per hour" },
        { "10 m/s",         "10 m/s         | 10 meters per second                             -> 36 km/h                  | 36 kilometers per hour" },
        // ── RPM ↔ Hz, y Hz prefijados ────────────────────────────────────────
        { "3000 rpm",       "3000 rpm       | 3000 revolutions per minute                      -> 50 Hz                    | 50 hertz" },
        { "50 Hz",          "50 Hz          | 50 hertz                                         -> 3000 rpm                 | 3000 revolutions per minute" },
        { "10 hz",          "10 Hz          | 10 hertz                                         -> 600 rpm                  | 600 revolutions per minute" }, // alias lowercase
        // Prefijos Hz: cadena kHz→Hz→rpm, MHz→kHz, GHz→MHz, THz→GHz
        { "10 kHz",         "10 kHz         | 10 kilohertz                                     -> 10000 Hz                 | 10000 hertz" },
        { "10 MHz",         "10 MHz         | 10 megahertz                                     -> 10000 kHz                | 10000 kilohertz" },
        { "10 GHz",         "10 GHz         | 10 gigahertz                                     -> 10000 MHz                | 10000 megahertz" },
        { "10 THz",         "10 THz         | 10 terahertz                                     -> 10000 GHz                | 10000 gigahertz" },
        { "10 Thz",         "10 THz         | 10 terahertz                                     -> 10000 GHz                | 10000 gigahertz" }, // casing alternativo: Thz → THz
        // ── mph / kmh aliases ────────────────────────────────────────────────
        { "60 mph",         "60 mph         | 60 miles per hour                                -> 96.56 kmh                | 96.56 kilometers per hour" },
        { "100 kmh",        "100 kmh        | 100 kilometers per hour                          -> 62.14 mph                | 62.14 miles per hour" },
        { "100 kmph",       "100 kmh        | 100 kilometers per hour                          -> 62.14 mph                | 62.14 miles per hour" },
        // ── Velocidad compuesta normalizada (unidades no estándar) ──────────────
        // Unidades con entrada directa en defaultTargets — FROM queda como el usuario escribió
        { "2 mi/s",         "2 mi/s         | 2 miles per second                               -> 3.22 km/s                | 3.22 kilometers per second" },
        { "60 mi/min",      "60 mi/min      | 60 miles per minute                              -> 96.56 km/min             | 96.56 kilometers per minute" },
        { "5 ft/s",         "5 ft/s         | 5 feet per second                                -> 1.52 m/s                 | 1.52 meters per second" },
        { "5 ft/min",       "5 ft/min       | 5 feet per minute                                -> 1.52 m/min               | 1.52 meters per minute" },
        { "100 km/min",     "100 km/min     | 100 kilometers per minute                        -> 3728.23 mi/h             | 3728.23 miles per hour" },
        // Unidades no estándar — FROM queda tal como lo escribió el usuario, TO a mi/h vía par dimensional
        { "2000000 mm/min", "2000000 mm/min | 2000000 millimeters per minute                   -> 74.56 mi/h               | 74.56 miles per hour" },
        { "10 mm/s",        "10 mm/s        | 10 millimeters per second                        -> 0.0224 mi/h              | 0.0224 miles per hour" },
        { "50 cm/s",        "50 cm/s        | 50 centimeters per second                        -> 1.12 mi/h                | 1.12 miles per hour" },
        { "10 Mm/min",      "10 Mm/min      | 10 megameters per minute                         -> 372822.72 mi/h           | 372822.72 miles per hour" },
        // ── Tasas de datos (bit/s ↔ byte/s) ─────────────────────────────────────
        { "1 Gbps",         "1 Gbps         | 1 gigabit per second                             -> 125 MB/s                 | 125 megabytes per second" },
        { "100 Mbps",       "100 Mbps       | 100 megabits per second                          -> 12.5 MB/s                | 12.5 megabytes per second" },
        { "10 kbps",        "10 kbps        | 10 kilobits per second                           -> 1.25 kB/s                | 1.25 kilobytes per second" },
        { "10 kB/s",        "10 kB/s        | 10 kilobytes per second                          -> 0.08 Mbps                | 0.08 megabits per second" },
        { "100 MB/s",       "100 MB/s       | 100 megabytes per second                         -> 800 Mbps                 | 800 megabits per second" },
        { "10 GB/s",        "10 GB/s        | 10 gigabytes per second                          -> 80 Gbps                  | 80 gigabits per second" },
        // ── Números grandes (normalizeUnits): from preservado en formato fijo ──────
        // El from queda en la unidad original; TryNormalize descompone el to.
        { "1000000 s",      "1000000 s      | 1000000 seconds                                  -> 11 day 13 h 46 min 40 s  | 11 days 13 hours 46 minutes 40 seconds" },
        { "1000000 ms",     "1000000 ms     | 1000000 milliseconds                             -> 16 min 40 s              | 16 minutes 40 seconds" },
        { "1000000 B",      "1000000 B      | 1000000 bytes                                    -> 1 MB                     | 1 megabyte" },
        { "1000000 kB",     "1000000 kB     | 1000000 kilobytes                                -> 1 GB                     | 1 gigabyte" },
    };

    [Theory]
    [MemberData(nameof(DefaultConversionCases))]
    public void DefaultConversion_Summary(string query, string expectedSummary) =>
        AssertSummary(query, expectedSummary);

    // ── Alias y formas canónicas ─────────────────────────────────────────────
    // Cualquier sinónimo (10h, 10hour, 10 hours, 10 foot, 10 feet, 10 mile, etc.)
    // debe normalizarse a la unidad canónica y producir el mismo resultado.

    public static TheoryData<string, string> UnitAliasCases => new() {
        // ── Tiempo — formas largas (auto-reverse de longNames) ───────────────
        { "10 hour",         "10 h            | 10 hours                                                   -> 600 min                | 600 minutes" },
        { "10 second",       "10 s            | 10 seconds                                                 -> 10000 ms               | 10000 milliseconds" },
        { "10 millisecond",  "10 ms           | 10 milliseconds                                            -> 0.01 s                 | 0.01 seconds" },
        // Tiempo — plurales (tokenAliases)
        { "10 hours",        "10 h            | 10 hours                                                   -> 600 min                | 600 minutes" },
        { "10 seconds",      "10 s            | 10 seconds                                                 -> 10000 ms               | 10000 milliseconds" },
        { "10 milliseconds", "10 ms           | 10 milliseconds                                            -> 0.01 s                 | 0.01 seconds" },
        { "10 minutes",      "10 min          | 10 minutes                                                 -> 600 s                  | 600 seconds" },
        { "10 weeks",        "10 week         | 10 weeks                                                   -> 70 day                 | 70 days" },
        { "10 years",        "10 year         | 10 years                                                   -> 3652.5 day             | 3652.5 days" },
        // Tiempo — capitalización variada
        { "10 Hour",         "10 h            | 10 hours                                                   -> 600 min                | 600 minutes" },
        { "10 Hours",        "10 h            | 10 hours                                                   -> 600 min                | 600 minutes" },
        { "10 HOURS",        "10 h            | 10 hours                                                   -> 600 min                | 600 minutes" },
        // ── Temperatura — formas largas y capitalización ─────────────────────
        { "100 celsius",     "100 °C          | 100 celsius                                                -> 212 °F                 | 212 fahrenheit" },
        { "100 fahrenheit",  "100 °F          | 100 fahrenheit                                             -> 37.78 °C               | 37.78 celsius" },
        { "100 Celsius",     "100 °C          | 100 celsius                                                -> 212 °F                 | 212 fahrenheit" },
        { "100 FAHRENHEIT",  "100 °F          | 100 fahrenheit                                             -> 37.78 °C               | 37.78 celsius" },
        // ── Longitud — formas singulares largas no cubiertas en DefaultConversionCases ─
        { "10 foot",         "10 ft           | 10 feet                                                    -> 3.05 m                 | 3.05 meters" },
        // ── Masa — formas singulares largas ──────────────────────────────────
        { "10 ounce",        "10 oz           | 10 ounces                                                  -> 283.5 g                | 283.5 grams" },
        // ── Volumen — formas largas y plurales ───────────────────────────────
        { "10 liter",        "10 L            | 10 litres                                                  -> 2.64 gallon            | 2.64 gallons" },
        { "10 litre",        "10 L            | 10 litres                                                  -> 2.64 gallon            | 2.64 gallons" },
        { "10 liters",       "10 L            | 10 litres                                                  -> 2.64 gallon            | 2.64 gallons" },
        { "10 litres",       "10 L            | 10 litres                                                  -> 2.64 gallon            | 2.64 gallons" },
        // ── Masa — formas largas singulares ──────────────────────────────────
        { "10 gram",         "10 g            | 10 grams                                                   -> 0.353 oz               | 0.353 ounces" },
        { "10 tonne",        "10 t            | 10 tonnes                                                  -> 22046.23 lb            | 22046.23 pounds" },
        // ── Electricidad — formas largas ──────────────────────────────────────
        { "10 watt",         "10 W            | 10 watts                                                   -> 0.01 kW                | 0.01 kilowatts" },
        // ── Presión — formas largas ───────────────────────────────────────────
        { "10 pascal",       "10 Pa           | 10 pascals                                                 -> 0.00145 psi" },
        // ── Fuerza — poundforce long form ────────────────────────────────────
        { "10poundforce",    "10 lbf          | 10 pound-forces                                            -> 44.48 N                | 44.48 newtons" },
        { "10 poundforces",  "10 lbf          | 10 pound-forces                                            -> 44.48 N                | 44.48 newtons" },
        // ── Datos — formas largas y plurales ─────────────────────────────────
        { "10 byte",         "10 B            | 10 bytes                                                   -> 0.01 kB                | 0.01 kilobytes" },
        { "10 bytes",        "10 B            | 10 bytes                                                   -> 0.01 kB                | 0.01 kilobytes" },
        { "10 kilobyte",     "10 kB           | 10 kilobytes                                               -> 0.01 MB                | 0.01 megabytes" },
        { "10 kilobytes",    "10 kB           | 10 kilobytes                                               -> 0.01 MB                | 0.01 megabytes" },
        { "10 megabyte",     "10 MB           | 10 megabytes                                               -> 0.01 GB                | 0.01 gigabytes" },
        { "10 megabytes",    "10 MB           | 10 megabytes                                               -> 0.01 GB                | 0.01 gigabytes" },
        { "10 gigabyte",     "10 GB           | 10 gigabytes                                               -> 0.01 TB                | 0.01 terabytes" },
        { "10 gigabytes",    "10 GB           | 10 gigabytes                                               -> 0.01 TB                | 0.01 terabytes" },
        { "10 terabyte",     "10 TB           | 10 terabytes                                               -> 10000 GB               | 10000 gigabytes" },
        // ── Volumen menor — plurales ──────────────────────────────────────────
        { "10 pints",        "10 pint         | 10 pints                                                   -> 20 cup                 | 20 cups" },
        { "10 quarts",       "10 quart        | 10 quarts                                                  -> 20 pint                | 20 pints" },
        { "10 cups",         "10 cup          | 10 cups                                                    -> 80 floz                | 80 fluid ounces" },
        { "10 tablespoons",  "10 tablespoon   | 10 tablespoons                                             -> 30 teaspoon            | 30 teaspoons" },
        { "10 teaspoons",    "10 teaspoon     | 10 teaspoons                                               -> 50 mL                  | 50 millilitres" },
        // ── Tiempo — decade alias ─────────────────────────────────────────────
        { "10 decades",      "10 decade       | 10 decades                                                 -> 100 year               | 100 years" },
        // ── Datos — TB longname ────────────────────────────────────────────────
        { "10 terabytes",    "10 TB           | 10 terabytes                                               -> 10000 GB               | 10000 gigabytes" },
        // ── Volumen menor — singulares canónicos ──────────────────────────────
        { "10 tablespoon",   "10 tablespoon   | 10 tablespoons                                             -> 30 teaspoon            | 30 teaspoons" },
        { "10 teaspoon",     "10 teaspoon     | 10 teaspoons                                               -> 50 mL                  | 50 millilitres" },
        // ── Fuerza — poundforce singular ──────────────────────────────────────
        { "10 poundforce",   "10 lbf          | 10 pound-forces                                            -> 44.48 N                | 44.48 newtons" },
        // ── Singular (1 unit) — long name suprimido cuando símbolo == longName ──
        // Regla: long name solo se muestra si aporta información (símbolo ≠ longName).
        // Suprimidos (símbolo==longName en singular): pint, quart, cup, ohm, year, decade, tablespoon, teaspoon.
        // Visibles: B/byte, kB/kilobyte, S/siemens, mol/mole, lbf/pound-force, hp/horsepower, floz/fluid ounce, etc.
        // ── Datos ─────────────────────────────────────────────────────────────
        { "1 B",             "1 B             | 1 byte                                                     -> 0.001 kB               | 0.001 kilobytes" },
        { "1 byte",          "1 B             | 1 byte                                                     -> 0.001 kB               | 0.001 kilobytes" },
        { "1 bytes",         "1 B             | 1 byte                                                     -> 0.001 kB               | 0.001 kilobytes" },
        { "1 kB",            "1 kB            | 1 kilobyte                                                 -> 0.001 MB               | 0.001 megabytes" },
        { "1 kilobyte",      "1 kB            | 1 kilobyte                                                 -> 0.001 MB               | 0.001 megabytes" },
        { "1 kilobytes",     "1 kB            | 1 kilobyte                                                 -> 0.001 MB               | 0.001 megabytes" },
        { "1 MB",            "1 MB            | 1 megabyte                                                 -> 0.001 GB               | 0.001 gigabytes" },
        { "1 megabyte",      "1 MB            | 1 megabyte                                                 -> 0.001 GB               | 0.001 gigabytes" },
        { "1 megabytes",     "1 MB            | 1 megabyte                                                 -> 0.001 GB               | 0.001 gigabytes" },
        { "1 GB",            "1 GB            | 1 gigabyte                                                 -> 0.001 TB               | 0.001 terabytes" },
        { "1 gigabyte",      "1 GB            | 1 gigabyte                                                 -> 0.001 TB               | 0.001 terabytes" },
        { "1 gigabytes",     "1 GB            | 1 gigabyte                                                 -> 0.001 TB               | 0.001 terabytes" },
        { "1 TB",            "1 TB            | 1 terabyte                                                 -> 1000 GB                | 1000 gigabytes" },
        { "1 terabyte",      "1 TB            | 1 terabyte                                                 -> 1000 GB                | 1000 gigabytes" },
        { "1 terabytes",     "1 TB            | 1 terabyte                                                 -> 1000 GB                | 1000 gigabytes" },
        // ── Tiempo adicional ──────────────────────────────────────────────────
        { "1 year",          "1 year                                                                       -> 365.25 day             | 365.25 days" },
        { "1 years",         "1 year                                                                       -> 365.25 day             | 365.25 days" },
        { "1 decade",        "1 decade                                                                     -> 10 year                | 10 years" },
        { "1 decades",       "1 decade                                                                     -> 10 year                | 10 years" },
        // ── Volumen menor ─────────────────────────────────────────────────────
        { "1 pint",          "1 pint                                                                       -> 2 cup                  | 2 cups" },
        { "1 pints",         "1 pint                                                                       -> 2 cup                  | 2 cups" },
        { "1 quart",         "1 quart                                                                      -> 2 pint                 | 2 pints" },
        { "1 quarts",        "1 quart                                                                      -> 2 pint                 | 2 pints" },
        { "1 cup",           "1 cup                                                                        -> 8 floz                 | 8 fluid ounces" },
        { "1 cups",          "1 cup                                                                        -> 8 floz                 | 8 fluid ounces" },
        { "1 floz",          "1 floz          | 1 fluid ounce                                              -> 29.57 mL               | 29.57 millilitres" },
        { "1 tbsp",          "1 tablespoon                                                                 -> 3 teaspoon             | 3 teaspoons" },
        { "1 tablespoon",    "1 tablespoon                                                                 -> 3 teaspoon             | 3 teaspoons" },
        { "1 tablespoons",   "1 tablespoon                                                                 -> 3 teaspoon             | 3 teaspoons" },
        { "1 tsp",           "1 teaspoon                                                                   -> 5 mL                   | 5 millilitres" },
        { "1 teaspoon",      "1 teaspoon                                                                   -> 5 mL                   | 5 millilitres" },
        { "1 teaspoons",     "1 teaspoon                                                                   -> 5 mL                   | 5 millilitres" },
        { "1 cc",            "1 cc            | 1 cubic centimeter                                         -> 1 mL                   | 1 millilitre" },
        // ── Fuerza ────────────────────────────────────────────────────────────
        { "1 lbf",           "1 lbf           | 1 pound-force                                              -> 4.45 N                 | 4.45 newtons" },
        { "1 poundforce",    "1 lbf           | 1 pound-force                                              -> 4.45 N                 | 4.45 newtons" },
        { "1 poundforces",   "1 lbf           | 1 pound-force                                              -> 4.45 N                 | 4.45 newtons" },
        // ── Potencia ─────────────────────────────────────────────────────────
        { "1 horsepower",    "1 hp            | 1 horsepower                                               -> 0.746 kW               | 0.746 kilowatts" },
        { "1 horsepowers",   "1 hp            | 1 horsepower                                               -> 0.746 kW               | 0.746 kilowatts" },
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
        { "0.01c",           "0.01 °C         | 0.01 celsius           > 10 mdegC  | 10 millicelsius       -> 32.02 °F               | 32.02 fahrenheit" },
        { "0.01f",           "0.01 °F         | 0.01 fahrenheit        > 10 mdegF  | 10 millifahrenheit    -> -17.77 °C              | -17.77 celsius" },
        // La parte decimal es tan pequeña que se absorbe en el redondeo a 2 decimales → "32"
        { "0.00001c",        "1e-5 °C         | 1e-5 celsius           > 10 udegC  | 10 microcelsius       -> 32 °F                  | 32 fahrenheit" },
        // 0.00001 = 1e-5: dos pasos SI de simplificación (m→mm→µm; g→mg→µg)
        { "0.00001 m",       "1e-5 m          | 1e-5 meters            > 10 um     | 10 micrometers        -> 3.280839895e-5 ft      | 3.280839895e-5 feet" },
        { "0.00001 g",       "1e-5 g          | 1e-5 grams             > 10 ug     | 10 micrograms         -> 3.527396195e-7 oz      | 3.527396195e-7 ounces" },
        // normalizeUnits (s): from preservado en notación científica; 1e-5 s = 0.01 ms
        { "0.00001 s",       "1e-5 s          | 1e-5 seconds                                               -> 0.01 ms                | 0.01 milliseconds" },
        // ── Electricidad ──────────────────────────────────────────────────────
        { "0.01W",           "0.01 W          | 0.01 watts             > 10 mW     | 10 milliwatts         -> 1e-5 kW                | 1e-5 kilowatts" },
        // C y F son aliases de degC/degF
        { "0.01C",           "0.01 °C         | 0.01 celsius           > 10 mdegC  | 10 millicelsius       -> 32.02 °F               | 32.02 fahrenheit" },
        { "0.01F",           "0.01 °F         | 0.01 fahrenheit        > 10 mdegF  | 10 millifahrenheit    -> -17.77 °C              | -17.77 celsius" },
        // ── Tiempo ────────────────────────────────────────────────────────────
        { "0.01h",           "0.01 h          | 0.01 hours                                                 -> 36 s                   | 36 seconds" },
        { "0.01 day",        "0.01 day        | 0.01 days                                                  -> 14 min 24 s            | 14 minutes 24 seconds" },
        { "0.01 min",        "0.01 min        | 0.01 minutes                                               -> 600 ms                 | 600 milliseconds" },
        { "0.01s",           "0.01 s          | 0.01 seconds                                               -> 10 ms                  | 10 milliseconds" },
        { "0.01ms",          "0.01 ms         | 0.01 milliseconds      > 10 us     | 10 microseconds       -> 1e-5 s                 | 1e-5 seconds" },
        { "0.001ms",         "0.001 ms        | 0.001 milliseconds     > 1 us      | 1 microsecond         -> 1e-6 s                 | 1e-6 seconds" },
        { "0.01Ms",          "0.01 Ms         | 0.01 megaseconds                                           -> 2 h 46 min 40 s        | 2 hours 46 minutes 40 seconds" },
        // ── Masa ──────────────────────────────────────────────────────────────
        { "0.01t",           "0.01 t          | 0.01 tonnes            > 10 mt     | 10 millitonnes        -> 22.05 lb               | 22.05 pounds" },
        { "0.01 g",          "0.01 g          | 0.01 grams             > 10 mg     | 10 milligrams         -> 3.527396195e-4 oz      | 3.527396195e-4 ounces" },
        { "0.01 oz",         "0.01 oz         | 0.01 ounces                                                -> 0.283 g                | 0.283 grams" },
        { "0.01 lb",         "0.01 lb         | 0.01 pounds                                                -> 0.00454 kg             | 0.00454 kilograms" },
        // ── Longitud ──────────────────────────────────────────────────────────
        { "0.01 m",          "0.01 m          | 0.01 meters            > 10 mm     | 10 millimeters        -> 0.0328 ft              | 0.0328 feet" },
        { "0.01 km",         "0.01 km         | 0.01 kilometers        > 10 m      | 10 meters             -> 0.00621 mile           | 0.00621 miles" },
        { "0.01 cm",         "0.01 cm         | 0.01 centimeters       > 100 um    | 100 micrometers       -> 0.00394 in             | 0.00394 inches" },
        { "0.01 mm",         "0.01 mm         | 0.01 millimeters       > 10 um     | 10 micrometers        -> 3.937007874e-4 in      | 3.937007874e-4 inches" },
        { "0.01 ft",         "0.01 ft         | 0.01 feet                                                  -> 0.00305 m              | 0.00305 meters" },
        { "0.01 in",         "0.01 in         | 0.01 inches                                                -> 0.0254 cm              | 0.0254 centimeters" },
        { "0.01 yard",       "0.01 yard       | 0.01 yards                                                 -> 0.00914 m              | 0.00914 meters" },
        { "0.01 mi",         "0.01 mi         | 0.01 miles                                                 -> 0.0161 km              | 0.0161 kilometers" },
        // ── Volumen ───────────────────────────────────────────────────────────
        { "0.01 l",          "0.01 L          | 0.01 litres            > 10 mL     | 10 millilitres        -> 0.00264 gallon         | 0.00264 gallons" },
        { "0.01 gal",        "0.01 gallon     | 0.01 gallons                                               -> 0.0379 L               | 0.0379 litres" },
        // ── Presión ───────────────────────────────────────────────────────────
        { "0.01 Pa",         "0.01 Pa         | 0.01 pascals           > 10 mPa                            -> 1.450377377e-6 psi" },
        { "0.01 bar",        "0.01 bar        | 0.01 bars              > 10 mbar                           -> 0.145 psi" },
        { "0.01 atm",        "0.01 atm        | 0.01 atmospheres                                           -> 0.0101 bar             | 0.0101 bars" },
        { "0.01 psi",        "0.01 psi                                                                     -> 6.894757293e-4 bar     | 6.894757293e-4 bars" },
        { "0.01 torr",       "0.01 torr                                                                    -> 0.01 mmHg" },
        { "0.01 mmHg",       "0.01 mmHg                                                                    -> 0.00133 kPa" },
        // ── Fuerza ────────────────────────────────────────────────────────────
        { "0.01 N",          "0.01 N          | 0.01 newtons           > 10 mN     | 10 millinewtons       -> 0.00225 lbf            | 0.00225 pound-forces" },
        { "0.01 lbf",        "0.01 lbf        | 0.01 pound-forces                                          -> 0.0445 N               | 0.0445 newtons" },
        { "0.01 kgf",        "0.01 kgf        | 0.01 kilogram-forces                                       -> 0.0981 N               | 0.0981 newtons" },
        { "0.01 dyn",        "0.01 dyn        | 0.01 dynes             > 10 mdyn   | 10 millidynes         -> 1e-4 mN                | 1e-4 millinewtons" },
        // ── Energía ───────────────────────────────────────────────────────────
        { "0.01 J",          "0.01 J          | 0.01 joules            > 10 mJ     | 10 millijoules        -> 1e-5 kJ                | 1e-5 kilojoules" },
        { "0.01 kJ",         "0.01 kJ         | 0.01 kilojoules        > 10 J      | 10 joules             -> 0.00278 Wh" },
        { "0.01 Wh",         "0.01 Wh                                  > 10 mWh                            -> 0.036 kJ               | 0.036 kilojoules" },
        { "0.01 eV",         "0.01 eV         | 0.01 electronvolts     > 10 meV    | 10 millielectronvolts -> 1.602176565e-21 J      | 1.602176565e-21 joules" },
        { "0.01 erg",        "0.01 erg                                 > 10 merg                           -> 1e-9 J                 | 1e-9 joules" },
        // ── Potencia ──────────────────────────────────────────────────────────
        { "0.01 hp",         "0.01 hp         | 0.01 horsepowers                                           -> 0.00746 kW             | 0.00746 kilowatts" },
        // ── Datos ─────────────────────────────────────────────────────────────
        { "0.01 B",          "0.01 B          | 0.01 bytes                                                 -> 1e-5 kB                | 1e-5 kilobytes" },
        { "0.01 kB",         "0.01 kB         | 0.01 kilobytes                                             -> 10 B                   | 10 bytes" },
        { "0.01 MB",         "0.01 MB         | 0.01 megabytes                                             -> 10 kB                  | 10 kilobytes" },
        { "0.01 GB",         "0.01 GB         | 0.01 gigabytes                                             -> 10 MB                  | 10 megabytes" },
        { "0.01 TB",         "0.01 TB         | 0.01 terabytes                                             -> 10 GB                  | 10 gigabytes" },
        // ── Tiempo adicional ──────────────────────────────────────────────────
        { "0.01 year",       "0.01 year       | 0.01 years                                                 -> 3 day 15 h 39 min 36 s | 3 days 15 hours 39 minutes 36 seconds" },
        { "0.01 decade",     "0.01 decade     | 0.01 decades                                               -> 0.1 year               | 0.1 years" },
        // ── Volumen menor ─────────────────────────────────────────────────────
        { "0.01 pint",       "0.01 pint       | 0.01 pints                                                 -> 0.02 cup               | 0.02 cups" },
        { "0.01 quart",      "0.01 quart      | 0.01 quarts                                                -> 0.02 pint              | 0.02 pints" },
        { "0.01 cup",        "0.01 cup        | 0.01 cups                                                  -> 0.08 floz              | 0.08 fluid ounces" },
        { "0.01 floz",       "0.01 floz       | 0.01 fluid ounces                                          -> 0.296 mL               | 0.296 millilitres" },
        { "0.01 tbsp",       "0.01 tablespoon | 0.01 tablespoons                                           -> 0.03 teaspoon          | 0.03 teaspoons" },
        { "0.01 tsp",        "0.01 teaspoon   | 0.01 teaspoons                                             -> 0.05 mL                | 0.05 millilitres" },
        { "0.01 cc",         "0.01 cc         | 0.01 cubic centimeters                                     -> 0.01 mL                | 0.01 millilitres" },
        // ── Ángulo ────────────────────────────────────────────────────────────
        { "0.01 rad",        "0.01 rad        | 0.01 radians           > 10 mrad   | 10 milliradians       -> 0.573 deg              | 0.573 degrees" },
        { "0.01 deg",        "0.01 deg        | 0.01 degrees           > 10 mdeg   | 10 millidegrees       -> 1.745329252e-4 rad     | 1.745329252e-4 radians" },
        { "0.01 grad",       "0.01 grad       | 0.01 gradians          > 10 mgrad  | 10 milligradians      -> 0.009 deg              | 0.009 degrees" },
        { "0.01 arcmin",     "0.01 arcmin     | 0.01 arcminutes                                            -> 0.6 arcsec             | 0.6 arcseconds" },
        // ── Área ──────────────────────────────────────────────────────────────
        { "0.01 m2",         "0.01 m2                                  > 10000 mm2                         -> 0.108 sqft" },
        { "0.01 sqft",       "0.01 sqft                                                                    -> 9.290304e-4 m2" },
        { "0.01 ha",         "0.01 ha         | 0.01 hectares                                              -> 0.0247 acre            | 0.0247 acres" },
        { "0.01 acre",       "0.01 acre       | 0.01 acres                                                 -> 0.00405 ha             | 0.00405 hectares" },
    };

    [Theory]
    [MemberData(nameof(UnitAliasCases))]
    public void UnitAlias_NormalizesToCanonical(string query, string expectedSummary) =>
        AssertSummary(query, expectedSummary);

    // ── Prefijos SI: cobertura completa por unidad ───────────────────────────
    // Cada TheoryData verifica la cadena completa de prefijos SI para una unidad:
    //   — "from": valor con prefijo + nombre largo si disponible en math.js
    //   — "to": conversión por defecto (defaultTargets o defaultPairs dimensional)
    //
    // Regla de auto-simplificación SI (math.format):
    //   coeff < 0.1  → baja de prefijo  (0.001 m → 1 mm)
    //   coeff ∈ [0.1, 1000] → sin cambio (0.1 m → 0.1 m, 1000 m → 1000 m)
    //   coeff > 1000 → sube de prefijo  (2000 m → 2 km)
    // Las unidades imperiales y no-SI nunca se auto-simplifican.
    // Excepción: normalizeUnits (s, ms, B, kB…) fuerza "expr to origUnit", protegiendo
    // el from de la simplificación upward. Ver TryNormalize en MathJsEngine.cs.
    //
    // defaultTarget para prefijos exóticos (no en defaultTargets):
    //   → defaultPairs dimensional → devuelve pair[0] (base SI).
    //   Ejemplo: "ym" no está en defaultTargets → pair["m","ft"] → target="m".
    //
    // Nota tonne: "ft" y "pt" en el sistema de tokens resuelven a foot y pint,
    // no a femtotonne y picotonne (restricción del sistema de tokens de math.js).

    // Collapses runs of spaces in expected — TheoryData rows pad cells with spaces
    // for visual column alignment; the actual FmtFull output uses single spaces.
    private static string CollapseSpaces(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private void AssertSummary(string query, string expected) {
        var item = GetConversionItem(query);
        Assert.Equal(CollapseSpaces(expected), FmtFull(item));
    }

    // ── metro ────────────────────────────────────────────────────────────────
    // Prefijos explícitos: mm→in, cm→in, km→mile. Resto →m via defaultPairs.
    public static TheoryData<string, string> Meter_SIPrefixCases => new() {
        { "1 m",  "1 m  | 1 meter      -> 3.28 ft      | 3.28 feet" },
        { "1 ym", "1 ym | 1 yoctometer -> 1e-24 m      | 1e-24 meters" },
        { "1 zm", "1 zm | 1 zeptometer -> 1e-21 m      | 1e-21 meters" },
        { "1 am", "1 am | 1 attometer  -> 1e-18 m      | 1e-18 meters" },
        { "1 fm", "1 fm | 1 femtometer -> 1e-15 m      | 1e-15 meters" },
        { "1 pm", "1 pm | 1 picometer  -> 1e-12 m      | 1e-12 meters" },
        { "1 nm", "1 nm | 1 nanometer  -> 1e-9 m       | 1e-9 meters" },
        { "1 um", "1 um | 1 micrometer -> 1e-6 m       | 1e-6 meters" },
        { "1 mm", "1 mm | 1 millimeter -> 0.0394 in    | 0.0394 inches" },
        { "1 cm", "1 cm | 1 centimeter -> 0.394 in     | 0.394 inches" },
        { "1 dm", "1 dm | 1 decimeter  -> 0.1 m        | 0.1 meters" },
        { "1 km", "1 km | 1 kilometer  -> 0.621 mile   | 0.621 miles" },
        { "1 Mm", "1 Mm | 1 megameter  -> 1000000 m    | 1000000 meters" },
        { "1 Gm", "1 Gm | 1 gigameter  -> 1000000000 m | 1000000000 meters" },
        { "1 Tm", "1 Tm | 1 terameter  -> 1e+12 m      | 1e+12 meters" },
        { "1 Pm", "1 Pm | 1 petameter  -> 1e+15 m      | 1e+15 meters" },
        { "1 Em", "1 Em | 1 exameter   -> 1e+18 m      | 1e+18 meters" },
        { "1 Zm", "1 Zm | 1 zettameter -> 1e+21 m      | 1e+21 meters" },
        { "1 Ym", "1 Ym | 1 yottameter -> 1e+24 m      | 1e+24 meters" },
    };

    [Theory, MemberData(nameof(Meter_SIPrefixCases))]
    public void Meter_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── gramo ────────────────────────────────────────────────────────────────
    // Prefijos explícitos: g→oz, mg→g, kg→lb. Resto →kg via defaultPairs.
    public static TheoryData<string, string> Gram_SIPrefixCases => new() {
        { "1 g",  "1 g  | 1 gram      -> 0.0353 oz     | 0.0353 ounces" },
        { "1 yg", "1 yg | 1 yoctogram -> 1e-27 kg      | 1e-27 kilograms" },
        { "1 zg", "1 zg | 1 zeptogram -> 1e-24 kg      | 1e-24 kilograms" },
        { "1 ag", "1 ag | 1 attogram  -> 1e-21 kg      | 1e-21 kilograms" },
        { "1 fg", "1 fg | 1 femtogram -> 1e-18 kg      | 1e-18 kilograms" },
        { "1 pg", "1 pg | 1 picogram  -> 1e-15 kg      | 1e-15 kilograms" },
        { "1 ng", "1 ng | 1 nanogram  -> 1e-12 kg      | 1e-12 kilograms" },
        { "1 ug", "1 ug | 1 microgram -> 1e-9 kg       | 1e-9 kilograms" },
        { "1 mg", "1 mg | 1 milligram -> 0.001 g       | 0.001 grams" },
        { "1 kg", "1 kg | 1 kilogram  -> 2.2 lb        | 2.2 pounds" },
        { "1 Mg", "1 Mg | 1 megagram  -> 1000 kg       | 1000 kilograms" },
        { "1 Gg", "1 Gg | 1 gigagram  -> 1000000 kg    | 1000000 kilograms" },
        { "1 Tg", "1 Tg | 1 teragram  -> 1000000000 kg | 1000000000 kilograms" },
        { "1 Pg", "1 Pg | 1 petagram  -> 1e+12 kg      | 1e+12 kilograms" },
        { "1 Eg", "1 Eg | 1 exagram   -> 1e+15 kg      | 1e+15 kilograms" },
        { "1 Zg", "1 Zg | 1 zettagram -> 1e+18 kg      | 1e+18 kilograms" },
        { "1 Yg", "1 Yg | 1 yottagram -> 1e+21 kg      | 1e+21 kilograms" },
    };

    [Theory, MemberData(nameof(Gram_SIPrefixCases))]
    public void Gram_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── vatio ────────────────────────────────────────────────────────────────
    // Prefijos explícitos: W→kW, kW→hp. Resto →W via defaultPairs.
    public static TheoryData<string, string> Watt_SIPrefixCases => new() {
        { "1 W",  "1 W  | 1 watt      -> 0.001 kW     | 0.001 kilowatts" },
        { "1 yW", "1 yW | 1 yoctowatt -> 1e-24 W      | 1e-24 watts" },
        { "1 zW", "1 zW | 1 zeptowatt -> 1e-21 W      | 1e-21 watts" },
        { "1 aW", "1 aW | 1 attowatt  -> 1e-18 W      | 1e-18 watts" },
        { "1 fW", "1 fW | 1 femtowatt -> 1e-15 W      | 1e-15 watts" },
        { "1 pW", "1 pW | 1 picowatt  -> 1e-12 W      | 1e-12 watts" },
        { "1 nW", "1 nW | 1 nanowatt  -> 1e-9 W       | 1e-9 watts" },
        { "1 uW", "1 uW | 1 microwatt -> 1e-6 W       | 1e-6 watts" },
        { "1 mW", "1 mW | 1 milliwatt -> 0.001 W      | 0.001 watts" },
        { "1 kW", "1 kW | 1 kilowatt  -> 1.34 hp      | 1.34 horsepowers" },
        { "1 MW", "1 MW | 1 megawatt  -> 1000000 W    | 1000000 watts" },
        { "1 GW", "1 GW | 1 gigawatt  -> 1000000000 W | 1000000000 watts" },
        { "1 TW", "1 TW | 1 terawatt  -> 1e+12 W      | 1e+12 watts" },
        { "1 PW", "1 PW | 1 petawatt  -> 1e+15 W      | 1e+15 watts" },
        { "1 EW", "1 EW | 1 exawatt   -> 1e+18 W      | 1e+18 watts" },
        { "1 ZW", "1 ZW | 1 zettawatt -> 1e+21 W      | 1e+21 watts" },
        { "1 YW", "1 YW | 1 yottawatt -> 1e+24 W      | 1e+24 watts" },
    };

    [Theory, MemberData(nameof(Watt_SIPrefixCases))]
    public void Watt_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── julio ────────────────────────────────────────────────────────────────
    // Prefijos explícitos: J→kJ, kJ→Wh (Wh sin longName en math.js). Resto →J via defaultPairs.
    public static TheoryData<string, string> Joule_SIPrefixCases => new() {
        { "1 J",  "1 J  | 1 joule      -> 0.001 kJ     | 0.001 kilojoules" },
        { "1 yJ", "1 yJ | 1 yoctojoule -> 1e-24 J      | 1e-24 joules" },
        { "1 zJ", "1 zJ | 1 zeptojoule -> 1e-21 J      | 1e-21 joules" },
        { "1 aJ", "1 aJ | 1 attojoule  -> 1e-18 J      | 1e-18 joules" },
        { "1 fJ", "1 fJ | 1 femtojoule -> 1e-15 J      | 1e-15 joules" },
        { "1 pJ", "1 pJ | 1 picojoule  -> 1e-12 J      | 1e-12 joules" },
        { "1 nJ", "1 nJ | 1 nanojoule  -> 1e-9 J       | 1e-9 joules" },
        { "1 uJ", "1 uJ | 1 microjoule -> 1e-6 J       | 1e-6 joules" },
        { "1 mJ", "1 mJ | 1 millijoule -> 0.001 J      | 0.001 joules" },
        { "1 kJ", "1 kJ | 1 kilojoule  -> 0.278 Wh" }, // Wh sin longName
        { "1 MJ", "1 MJ | 1 megajoule  -> 1000000 J    | 1000000 joules" },
        { "1 GJ", "1 GJ | 1 gigajoule  -> 1000000000 J | 1000000000 joules" },
        { "1 TJ", "1 TJ | 1 terajoule  -> 1e+12 J      | 1e+12 joules" },
        { "1 PJ", "1 PJ | 1 petajoule  -> 1e+15 J      | 1e+15 joules" },
        { "1 EJ", "1 EJ | 1 exajoule   -> 1e+18 J      | 1e+18 joules" },
        { "1 ZJ", "1 ZJ | 1 zettajoule -> 1e+21 J      | 1e+21 joules" },
        { "1 YJ", "1 YJ | 1 yottajoule -> 1e+24 J      | 1e+24 joules" },
    };

    [Theory, MemberData(nameof(Joule_SIPrefixCases))]
    public void Joule_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── newton ───────────────────────────────────────────────────────────────
    // Prefijos explícitos: N→lbf, lbf→N, kN→N. Resto →N via defaultPairs.
    public static TheoryData<string, string> Newton_SIPrefixCases => new() {
        { "1 N",  "1 N  | 1 newton      -> 0.225 lbf    | 0.225 pound-forces" },
        { "1 yN", "1 yN | 1 yoctonewton -> 1e-24 N      | 1e-24 newtons" },
        { "1 zN", "1 zN | 1 zeptonewton -> 1e-21 N      | 1e-21 newtons" },
        { "1 aN", "1 aN | 1 attonewton  -> 1e-18 N      | 1e-18 newtons" },
        { "1 fN", "1 fN | 1 femtonewton -> 1e-15 N      | 1e-15 newtons" },
        { "1 pN", "1 pN | 1 piconewton  -> 1e-12 N      | 1e-12 newtons" },
        { "1 nN", "1 nN | 1 nanonewton  -> 1e-9 N       | 1e-9 newtons" },
        { "1 uN", "1 uN | 1 micronewton -> 1e-6 N       | 1e-6 newtons" },
        { "1 mN", "1 mN | 1 millinewton -> 0.001 N      | 0.001 newtons" },
        { "1 kN", "1 kN | 1 kilonewton  -> 1000 N       | 1000 newtons" },
        { "1 MN", "1 MN | 1 meganewton  -> 1000000 N    | 1000000 newtons" },
        { "1 GN", "1 GN | 1 giganewton  -> 1000000000 N | 1000000000 newtons" },
        { "1 TN", "1 TN | 1 teranewton  -> 1e+12 N      | 1e+12 newtons" },
        { "1 PN", "1 PN | 1 petanewton  -> 1e+15 N      | 1e+15 newtons" },
        { "1 EN", "1 EN | 1 exanewton   -> 1e+18 N      | 1e+18 newtons" },
        { "1 ZN", "1 ZN | 1 zettanewton -> 1e+21 N      | 1e+21 newtons" },
        { "1 YN", "1 YN | 1 yottanewton -> 1e+24 N      | 1e+24 newtons" },
    };

    [Theory, MemberData(nameof(Newton_SIPrefixCases))]
    public void Newton_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── litro ────────────────────────────────────────────────────────────────
    // L usa grupo LONG de math.js con ortografía "litre" (británico).
    // Prefijo explícito: L→gallon. Resto →L via defaultPairs.
    public static TheoryData<string, string> Litre_SIPrefixCases => new() {
        { "1 L",  "1 L  | 1 litre      -> 0.264 gallon | 0.264 gallons" },
        { "1 yL", "1 yL | 1 yoctolitre -> 1e-24 L      | 1e-24 litres" },
        { "1 zL", "1 zL | 1 zeptolitre -> 1e-21 L      | 1e-21 litres" },
        { "1 aL", "1 aL | 1 attolitre  -> 1e-18 L      | 1e-18 litres" },
        { "1 fL", "1 fL | 1 femtolitre -> 1e-15 L      | 1e-15 litres" },
        { "1 pL", "1 pL | 1 picolitre  -> 1e-12 L      | 1e-12 litres" },
        { "1 nL", "1 nL | 1 nanolitre  -> 1e-9 L       | 1e-9 litres" },
        { "1 uL", "1 uL | 1 microlitre -> 1e-6 L       | 1e-6 litres" },
        { "1 mL", "1 mL | 1 millilitre -> 0.001 L      | 0.001 litres" },
        { "1 kL", "1 kL | 1 kilolitre  -> 1000 L       | 1000 litres" },
        { "1 ML", "1 ML | 1 megalitre  -> 1000000 L    | 1000000 litres" },
        { "1 GL", "1 GL | 1 gigalitre  -> 1000000000 L | 1000000000 litres" },
        { "1 TL", "1 TL | 1 teralitre  -> 1e+12 L      | 1e+12 litres" },
        { "1 PL", "1 PL | 1 petalitre  -> 1e+15 L      | 1e+15 litres" },
        { "1 EL", "1 EL | 1 exalitre   -> 1e+18 L      | 1e+18 litres" },
        { "1 ZL", "1 ZL | 1 zettalitre -> 1e+21 L      | 1e+21 litres" },
        { "1 YL", "1 YL | 1 yottalitre -> 1e+24 L      | 1e+24 litres" },
    };

    [Theory, MemberData(nameof(Litre_SIPrefixCases))]
    public void Litre_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── radián ───────────────────────────────────────────────────────────────
    // Prefijo explícito: rad→deg. Resto →rad via defaultPairs.
    public static TheoryData<string, string> Radian_SIPrefixCases => new() {
        { "1 rad",  "1 rad  | 1 radian      -> 57.3 deg       | 57.3 degrees" },
        { "1 yrad", "1 yrad | 1 yoctoradian -> 1e-24 rad      | 1e-24 radians" },
        { "1 zrad", "1 zrad | 1 zeptoradian -> 1e-21 rad      | 1e-21 radians" },
        { "1 arad", "1 arad | 1 attoradian  -> 1e-18 rad      | 1e-18 radians" },
        { "1 frad", "1 frad | 1 femtoradian -> 1e-15 rad      | 1e-15 radians" },
        { "1 prad", "1 prad | 1 picoradian  -> 1e-12 rad      | 1e-12 radians" },
        { "1 nrad", "1 nrad | 1 nanoradian  -> 1e-9 rad       | 1e-9 radians" },
        { "1 urad", "1 urad | 1 microradian -> 1e-6 rad       | 1e-6 radians" },
        { "1 mrad", "1 mrad | 1 milliradian -> 0.001 rad      | 0.001 radians" },
        { "1 krad", "1 krad | 1 kiloradian  -> 1000 rad       | 1000 radians" },
        { "1 Mrad", "1 Mrad | 1 megaradian  -> 1000000 rad    | 1000000 radians" },
        { "1 Grad", "1 Grad | 1 gigaradian  -> 1000000000 rad | 1000000000 radians" },
        { "1 Trad", "1 Trad | 1 teraradian  -> 1e+12 rad      | 1e+12 radians" },
        { "1 Prad", "1 Prad | 1 petaradian  -> 1e+15 rad      | 1e+15 radians" },
        { "1 Erad", "1 Erad | 1 exaradian   -> 1e+18 rad      | 1e+18 radians" },
        { "1 Zrad", "1 Zrad | 1 zettaradian -> 1e+21 rad      | 1e+21 radians" },
        { "1 Yrad", "1 Yrad | 1 yottaradian -> 1e+24 rad      | 1e+24 radians" },
    };

    [Theory, MemberData(nameof(Radian_SIPrefixCases))]
    public void Radian_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── hercio ───────────────────────────────────────────────────────────────
    // Prefijos explícitos: Hz→rpm, mHz→Hz, kHz→Hz, MHz→kHz, …, YHz→ZHz.
    // Prefijos sub-mHz (yHz…uHz) no tienen defaultTarget: no producen conversión.
    // "hertz" no pluraliza; tampoco sus compuestos (kilohertz, megahertz…). Ver Pluralize.
    public static TheoryData<string, string> Hertz_SIPrefixCases => new() {
        { "1 Hz",  "1 Hz  | 1 hertz      -> 60 rpm   | 60 revolutions per minute" },
        { "1 mHz", "1 mHz | 1 millihertz -> 0.001 Hz | 0.001 hertz" },
        { "1 kHz", "1 kHz | 1 kilohertz  -> 1000 Hz  | 1000 hertz" },
        { "1 MHz", "1 MHz | 1 megahertz  -> 1000 kHz | 1000 kilohertz" },
        { "1 GHz", "1 GHz | 1 gigahertz  -> 1000 MHz | 1000 megahertz" },
        { "1 THz", "1 THz | 1 terahertz  -> 1000 GHz | 1000 gigahertz" },
        { "1 PHz", "1 PHz | 1 petahertz  -> 1000 THz | 1000 terahertz" },
        { "1 EHz", "1 EHz | 1 exahertz   -> 1000 PHz | 1000 petahertz" },
        { "1 ZHz", "1 ZHz | 1 zettahertz -> 1000 EHz | 1000 exahertz" },
        { "1 YHz", "1 YHz | 1 yottahertz -> 1000 ZHz | 1000 zettahertz" },
    };

    [Theory, MemberData(nameof(Hertz_SIPrefixCases))]
    public void Hertz_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── tonelada ─────────────────────────────────────────────────────────────
    // Prefijo explícito: t→lb. Resto →kg via defaultPairs.
    // "ft" y "pt" resuelven a foot y pint (no a femtotonne/picotonne).
    public static TheoryData<string, string> Tonne_SIPrefixCases => new() {
        { "1 t",  "1 t  | 1 tonne      -> 2204.62 lb    | 2204.62 pounds" },
        { "1 yt", "1 yt | 1 yoctotonne -> 1e-21 kg      | 1e-21 kilograms" },
        { "1 zt", "1 zt | 1 zeptotonne -> 1e-18 kg      | 1e-18 kilograms" },
        { "1 at", "1 at | 1 attotonne  -> 1e-15 kg      | 1e-15 kilograms" },
        { "1 ft", "1 ft | 1 foot       -> 0.305 m       | 0.305 meters" }, // ft=foot, no femtotonne
        { "1 pt", "1 pt                -> 0.473 L       | 0.473 litres" }, // pt=pint, no picotonne
        { "1 nt", "1 nt | 1 nanotonne  -> 1e-6 kg       | 1e-6 kilograms" },
        { "1 ut", "1 ut | 1 microtonne -> 0.001 kg      | 0.001 kilograms" },
        { "1 mt", "1 mt | 1 millitonne -> 1 kg          | 1 kilogram" },
        { "1 kt", "1 kt | 1 kilotonne  -> 1000000 kg    | 1000000 kilograms" },
        { "1 Mt", "1 Mt | 1 megatonne  -> 1000000000 kg | 1000000000 kilograms" },
        { "1 Gt", "1 Gt | 1 gigatonne  -> 1e+12 kg      | 1e+12 kilograms" },
        { "1 Tt", "1 Tt | 1 teratonne  -> 1e+15 kg      | 1e+15 kilograms" },
        { "1 Pt", "1 Pt | 1 petatonne  -> 1e+18 kg      | 1e+18 kilograms" },
        { "1 Et", "1 Et | 1 exatonne   -> 1e+21 kg      | 1e+21 kilograms" },
        { "1 Zt", "1 Zt | 1 zettatonne -> 1e+24 kg      | 1e+24 kilograms" },
        { "1 Yt", "1 Yt | 1 yottatonne -> 1e+27 kg      | 1e+27 kilograms" },
    };

    [Theory, MemberData(nameof(Tonne_SIPrefixCases))]
    public void Tonne_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── segundo ──────────────────────────────────────────────────────────────
    // normalizeUnits: TryNormalize fuerza "expr to origUnit" (sin upward simplification).
    // Prefijos sub-ms → ms (último eslabón). Supra-s → descomposición natural.
    public static TheoryData<string, string> Second_SIPrefixCases => new() {
        { "1 s",  "1 s  | 1 second      -> 1000 ms                         | 1000 milliseconds" },
        { "1 ys", "1 ys | 1 yoctosecond -> 1e-21 ms                        | 1e-21 milliseconds" },
        { "1 zs", "1 zs | 1 zeptosecond -> 1e-18 ms                        | 1e-18 milliseconds" },
        { "1 as", "1 as | 1 attosecond  -> 1e-15 ms                        | 1e-15 milliseconds" },
        { "1 fs", "1 fs | 1 femtosecond -> 1e-12 ms                        | 1e-12 milliseconds" },
        { "1 ps", "1 ps | 1 picosecond  -> 1e-9 ms                         | 1e-9 milliseconds" },
        { "1 ns", "1 ns | 1 nanosecond  -> 1e-6 ms                         | 1e-6 milliseconds" },
        { "1 us", "1 us | 1 microsecond -> 0.001 ms                        | 0.001 milliseconds" },
        { "1 ms", "1 ms | 1 millisecond -> 0.001 s                         | 0.001 seconds" },
        { "1 ks", "1 ks | 1 kilosecond  -> 16 min 40 s                     | 16 minutes 40 seconds" },
        { "1 Ms", "1 Ms | 1 megasecond  -> 11 day 13 h 46 min 40 s         | 11 days 13 hours 46 minutes 40 seconds" },
        { "1 Gs", "1 Gs | 1 gigasecond  -> 31 year 251 day 7 h 46.67 min   | 31 years 251 days 7 hours 46.67 minutes" },
        { "1 Ts", "1 Ts | 1 terasecond  -> 31688 year 32 day 1 h 46.67 min | 31688 years 32 days 1 hour 46.67 minutes" },
        { "1 Ps", "1 Ps | 1 petasecond  -> 31688087 year 297 day           | 31688087 years 297 days" },
        { "1 Es", "1 Es | 1 exasecond   -> 31688087814 year                | 31688087814 years" },
        { "1 Zs", "1 Zs | 1 zettasecond -> 31688087814028 year             | 31688087814028 years" },
        { "1 Ys", "1 Ys | 1 yottasecond -> 31688087814028948 year          | 31688087814028948 years" },
    };

    [Theory, MemberData(nameof(Second_SIPrefixCases))]
    public void Second_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── byte ─────────────────────────────────────────────────────────────────
    // normalizeUnits: TryNormalize fuerza "expr to origUnit".
    // La cadena best_unit llega hasta TB; PB-YB se expresan en TB.
    // PB+ sin longName (math.js no deriva nombre largo para esos prefijos de B).
    public static TheoryData<string, string> Byte_SIPrefixCases => new() {
        { "1 B",  "1 B  | 1 byte     -> 0.001 kB         | 0.001 kilobytes" },
        { "1 kB", "1 kB | 1 kilobyte -> 0.001 MB         | 0.001 megabytes" },
        { "1 MB", "1 MB | 1 megabyte -> 0.001 GB         | 0.001 gigabytes" },
        { "1 GB", "1 GB | 1 gigabyte -> 0.001 TB         | 0.001 terabytes" },
        { "1 TB", "1 TB | 1 terabyte -> 1000 GB          | 1000 gigabytes" },
        { "1 PB", "1 PB              -> 1000 TB          | 1000 terabytes" },
        { "1 EB", "1 EB              -> 1000000 TB       | 1000000 terabytes" },
        { "1 ZB", "1 ZB              -> 1000000000 TB    | 1000000000 terabytes" },
        { "1 YB", "1 YB              -> 1000000000000 TB | 1000000000000 terabytes" },
    };

    [Theory, MemberData(nameof(Byte_SIPrefixCases))]
    public void Byte_SIPrefixes_DefaultConversion(string query, string expected) => AssertSummary(query, expected);

    // ── Velocidad compuesta — nuevas unidades ────────────────────────────────
    public static TheoryData<string, string> NewCompoundUnitCases => new() {
        // yard/h, ft/h — en defaultTargets, FROM queda como el usuario escribió
        { "10 yard/h", "10 yard/h | 10 yards per hour       -> 0.00914 km/h        | 0.00914 kilometers per hour" },
        { "5 ft/h",    "5 ft/h    | 5 feet per hour         -> 0.00152 km/h        | 0.00152 kilometers per hour" },
        // mm/h, cm/h — FROM queda tal como lo escribió el usuario, TO a mi/h vía par dimensional
        { "10 mm/h",   "10 mm/h   | 10 millimeters per hour -> 6.213711922e-6 mi/h | 6.213711922e-6 miles per hour" },
        { "10 cm/h",   "10 cm/h   | 10 centimeters per hour -> 6.213711922e-5 mi/h | 6.213711922e-5 miles per hour" },
    };

    [Theory]
    [MemberData(nameof(NewCompoundUnitCases))]
    public void NewCompoundUnit_DefaultConversion(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(CollapseSpaces(expectedSummary), summary);
    }

    // ── Conversión explícita de unidades compuestas — long names en TO ───────
    public static TheoryData<string, string> ExplicitCompoundConversionCases => new() {
        // FROM siempre en la unidad compuesta original (no auto-simplificada a custom unit)
        { "10 mi/ms to m/h",   "10 mi/ms  | 10 miles per millisecond -> 5.7936384e+10 m/h   | 5.7936384e+10 meters per hour" },
        { "10 yard/h to mi/s", "10 yard/h | 10 yards per hour        -> 1.578282828e-6 mi/s | 1.578282828e-6 miles per second" },
        { "10 mi/s to ft/s",   "10 mi/s   | 10 miles per second      -> 52800 ft/s          | 52800 feet per second" },
    };

    [Theory]
    [MemberData(nameof(ExplicitCompoundConversionCases))]
    public void ExplicitCompoundConversion_ShowsLongNameInTo(string query, string expectedSummary) {
        var item = GetConversionItem(query);
        var summary = $"{Fmt(item.FromShort, item.FromLong)} -> {Fmt(item.ToShort, item.ToLong)}";
        Assert.Equal(CollapseSpaces(expectedSummary), summary);
    }

    // ── forceAmbiguous: emite AmbiguityHint aun resolviendo al símbolo forzado ──
    // "mS" = millisiemens en math.js, pero forceAmbiguous lo redirige a ms (milliseconds)
    // y además marca la resolución como ambigua para avisar al usuario del conflicto.
    // Ver unit-config.json: "forceAmbiguous": { "mS": "ms", "MS": "ms" }.
    [Fact]
    public void ForceAmbiguous_mS_ResolvesToMilliseconds_WithAmbiguityHint() {
        var (item, search) = GetConversionItemWithSearch("10 mS");
        Assert.Equal("10 ms", item.FromShort);
        Assert.NotNull(search.LastHint);
        Assert.Contains("Maybe you meant", search.LastHint);
    }

    // ── ambiguityOverrides: resuelve al símbolo canónico preferido ──────────────
    // A diferencia de forceAmbiguous, ambiguityOverrides resuelve la ambigüedad a un
    // único símbolo "canónico" preferido. Aun así emite AmbiguityHint porque sigue
    // habiendo alternativas que el usuario podría haber querido.
    // "10 pa" → Pa (pascal) vía override; con hint de PA/pA como alternativas.
    [Fact]
    public void AmbiguityOverride_pa_ResolvesToPascal() {
        var (item, search) = GetConversionItemWithSearch("10 pa");
        Assert.Equal("10 Pa", item.FromShort);
        Assert.NotNull(search.LastHint);
    }

    // "10 mhz" → MHz (megahertz) vía override; alternativa mHz (millihertz) en hint
    [Fact]
    public void AmbiguityOverride_mhz_ResolvesToMegahertz() {
        var (item, search) = GetConversionItemWithSearch("10 mhz");
        Assert.Equal("10 MHz", item.FromShort);
        Assert.NotNull(search.LastHint);
        Assert.Contains("mHz", search.LastHint);
    }

    // ── minute: display short "min" (displayNames) ──────────────────────────────
    // Para "1 minute" (singular), el FromShort se acorta a "min" vía displayNames,
    // pero el FromLong puede quedar null cuando math.js no deriva un nombre largo
    // distinto del símbolo del usuario. El ToShort/ToLong se derivan normalmente.
    [Fact]
    public void Minute_Singular_ShortForm() {
        var item = GetConversionItem("1 minute");
        Assert.Equal("1 min", item.FromShort);
        Assert.Equal("60 s", item.ToShort);
        Assert.Equal("60 seconds", item.ToLong);
    }

    // ── complex_conversion con LHS aritmético ──────────────────────────────────
    // Expresiones como "10 m + 5 cm to ft" tienen LHS aritmético (no "número * símbolo"),
    // por lo que NormFromUnit queda null y el summary no muestra celda intermedia.
    [Fact]
    public void ComplexConversion_ArithmeticLhs_ToFeet() {
        var item = GetConversionItem("10 m + 5 cm to ft");
        Assert.Contains("ft", item.ToShort);
        Assert.Null(item.NormFromShort); // LHS aritmético no produce norm-from cell
    }
}

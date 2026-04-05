<!--
CONTEXTO PARA SESIÓN DE CLAUDE CODE
=====================================
Este documento es el catálogo editorial de las ~250 unidades base que math.js expone
en Yottacast (conversor/calculadora). El objetivo es curar qué unidades se aceptan,
cuáles se bloquean y cómo se muestran al usuario.

TAREA:
Revisad cada sección y marcad en la columna "Decisión" una de estas opciones:
  - ✅ mantener  — la unidad está bien tal cual
  - ❌ bloquear  — no debe aceptarse como input (demasiado oscura); añadir a `blocked`
  - 🔤 alias     — añadir alias de entrada; especificad cuál en "Notas"
  - 🏷 display   — cambiar el nombre de display; especificad cuál en "Notas"

Una vez rellenado, ejecutad este prompt en Claude Code:
"Lee docs/unit-catalog.md y actualiza unit-config.json con las decisiones marcadas
(blocked, tokenAliases, inputAliases, displayNames). Luego ejecuta los tests."

Ver también: docs/search-calculator.md (arquitectura completa del sistema).
-->

# Catálogo de unidades

Catálogo de las ~250 unidades base que math.js expone. No incluye las miles de combinaciones prefijo+unidad (esas son válidas automáticamente si la unidad base lo es).

La configuración resultante vive en `Yottacast.Core/Search/Calculator/unit-config.json`.

**Leyenda de tipos:**
- 🌍 cotidiana (SI / uso universal)
- 🇺🇸 EEUU / 🇬🇧 UK (sistema imperial)
- 🔬 científica
- 📐 topografía / agrimensura (muy técnica)
- 🏭 industrial
- 📜 histórica / obsoleta
- 💊 médica / farmacéutica
- ❓ oscura / sin longName en math.js

---

## Temperatura

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| degC, celsius | degree Celsius | 🌍 cotidiana | ✅ mantener | Input canónico. inputAliases: °c, ºc → degC; tokenAlias: c → degC |
| degF, fahrenheit | degree Fahrenheit | 🇺🇸 EEUU | ✅ mantener | inputAliases: °f, ºf → degF; tokenAlias: f → degF |
| K, kelvin | kelvin | 🔬 científica | ✅ mantener | |
| degR, rankine | degree Rankine | 🔬 científica EEUU | ✅ mantener | Temperatura absoluta Fahrenheit |

---

## Longitud / Distancia

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| m, meter | meter | 🌍 cotidiana | ✅ mantener | |
| inch, in | inch | 🇺🇸 EEUU | ✅ mantener | |
| foot, ft | foot | 🇺🇸 EEUU | ✅ mantener | |
| yard, yd | yard | 🇺🇸 EEUU | ✅ mantener | |
| mile | mile | 🇺🇸 EEUU | ✅ mantener | |
| nauticalMile, nmi | nautical mile | 🏭 náutica | ✅ mantener | |
| angstrom, Å | angstrom | 🔬 científica | ✅ mantener | |
| mil | mil (thousandth of inch) | 🏭 industrial | ✅ mantener | |
| li | link (surveyor) | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| rd | rod | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| ch | chain | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| link, links | link | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| rod, rods | rod | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| chain, chains | chain | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| fathom | fathom | 🏭 náutica | ✅ mantener | |
| furlong | furlong | 📜 histórica | — | |
| parsec | parsec | 🔬 científica | ✅ mantener | |
| lightyear, ly | light year | 🔬 científica | ✅ mantener | |
| astronomicalUnit, AU | astronomical unit | 🔬 científica | ✅ mantener | |
| bohr, a0 | bohr radius | 🔬 científica | ✅ mantener | |
| planckLength | Planck length | 🔬 científica | ✅ mantener | |

---

## Masa / Peso

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| g, gram | gram | 🌍 cotidiana | ✅ mantener | |
| tonne, t | metric tonne | 🌍 cotidiana | ✅ mantener | |
| lb, lbm, pound | pound | 🇺🇸 EEUU | ✅ mantener | |
| oz, ounce | ounce | 🇺🇸 EEUU | ✅ mantener | |
| ton | short ton | 🇺🇸 EEUU | ✅ mantener | |
| longton | long ton | 🇬🇧 UK | ✅ mantener | |
| hundredweight, cwt | hundredweight | 🇬🇧 UK | ✅ mantener | |
| grain, gr | grain | 💊 farmacéutica | ✅ mantener | |
| carat, ct | carat | 🏭 joyería | ✅ mantener | |
| stone | stone | 🇬🇧 UK | ✅ mantener | |
| planckMass | Planck mass | 🔬 científica | ✅ mantener | |
| atomicMass, u | atomic mass unit | 🔬 científica | ✅ mantener | |
| electronMass | electron mass | 🔬 científica | ✅ mantener | |

---

## Volumen

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| L, l, litre, liter | liter | 🌍 cotidiana | ✅ mantener | |
| mL, ml | milliliter | 🌍 cotidiana | ✅ mantener | |
| gallon, gal | gallon (US) | 🇺🇸 EEUU | ✅ mantener | |
| quart | quart | 🇺🇸 EEUU | ✅ mantener | tokenAlias: `quarts` → quart |
| pint | pint | 🇺🇸 EEUU | ✅ mantener | tokenAlias: `pints` → pint |
| cup | cup | 🇺🇸 EEUU | ✅ mantener | tokenAlias: `cups` → cup |
| floz | fluid ounce | 🇺🇸 EEUU | ✅ mantener | |
| tablespoon | tablespoon | 🇺🇸 EEUU | ✅ mantener | Símbolo canónico en math.js. tokenAliases: `tbsp` → tablespoon, `tablespoons` → tablespoon |
| teaspoon | teaspoon | 🇺🇸 EEUU | ✅ mantener | Símbolo canónico en math.js. tokenAliases: `tsp` → teaspoon, `teaspoons` → teaspoon |
| obl | oil barrel | 🏭 industrial | ❌ bloquear | Ya en `blocked` |
| lt | UK long ton (liq) | 📜 histórica | ❌ bloquear | Ya en `blocked` |
| gi, gill, gills | gill | 📜 histórica | ❌ bloquear | Ya en `blocked` |
| cp, fldr, fluiddram, fluiddrams | dram / fluidram | 💊 farmacéutica | ❌ bloquear | Ya en `blocked` |
| gtt, gtts | gota | 💊 farmacéutica | ❌ bloquear | Ya en `blocked` |
| minim, minims | minim | 💊 farmacéutica | ❌ bloquear | Ya en `blocked` |
| cc | cubic centimeter | 🌍 cotidiana | ✅ mantener | Sinónimo de mL |
| drop | drop | 💊 farmacéutica | — | |

---

## Tiempo

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| s, sec, second | second | 🌍 cotidiana | ✅ mantener | |
| min, minute | minute | 🌍 cotidiana | ✅ mantener | |
| h, hr, hour | hour | 🌍 cotidiana | ✅ mantener | |
| day | day | 🌍 cotidiana | ✅ mantener | tokenAlias: `d` → day, `days` → day |
| week | week | 🌍 cotidiana | ✅ mantener | tokenAlias: `weeks` → week |
| month | month | 🌍 cotidiana | ❌ bloquear | En `blocked`. La duración variable (28-31 días) hace que las conversiones sean confusas |
| year | year | 🌍 cotidiana | ✅ mantener | tokenAlias: `years` → year |
| decade | decade | 🌍 cotidiana | ✅ mantener | tokenAlias: `decades` → decade |
| century | century | 🌍 cotidiana | ✅ mantener | |
| millennium | millennium | 🌍 cotidiana | ✅ mantener | |
| planckTime | Planck time | 🔬 científica | ✅ mantener | |

---

## Área

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| m2 | square meter | 🌍 cotidiana | ✅ mantener | |
| ha, hectare | hectare | 🌍 cotidiana | ✅ mantener | |
| acre | acre | 🇺🇸 EEUU | ✅ mantener | |
| sqin | square inch | 🇺🇸 EEUU | ✅ mantener | |
| sqft | square foot | 🇺🇸 EEUU | ✅ mantener | |
| sqyd | square yard | 🇺🇸 EEUU | ✅ mantener | |
| sqmi | square mile | 🇺🇸 EEUU | ✅ mantener | |
| sqrd | square rod | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| sqch | square chain | 📐 topografía | ❌ bloquear | Ya en `blocked` |
| sqmil | square mil | 🏭 industrial | ❌ bloquear | Ya en `blocked` |

---

## Ángulo

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| rad, radian | radian | 🌍 cotidiana | ✅ mantener | |
| deg, degree | degree | 🌍 cotidiana | ✅ mantener | |
| grad, gradian | gradian | 🔬 científica | ✅ mantener | |
| cycle | cycle | 🔬 científica | ✅ mantener | |
| arcmin, arcminute | arcminute | 🔬 científica | ✅ mantener | |
| arcsec, arcsecond | arcsecond | 🔬 científica | ✅ mantener | |

---

## Energía

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| J, joule | joule | 🌍 cotidiana | ✅ mantener | |
| eV, electronvolt | electronvolt | 🔬 científica | ✅ mantener | |
| Wh | watt-hour | 🌍 cotidiana | ✅ mantener | |
| BTU, btu | British thermal unit | 🇺🇸 EEUU | ❌ bloquear | En `blocked`. Cadena práctica: J → kJ → Wh |
| cal, calorie | calorie | 🌍 cotidiana | ✅ mantener | No disponible en esta build de math.js; da error de símbolo desconocido |
| kcal | kilocalorie | 🌍 cotidiana | ✅ mantener | No disponible en esta build de math.js; da error de símbolo desconocido |
| erg | erg | 🔬 científica | ✅ mantener | |
| planckEnergy | Planck energy | 🔬 científica | ✅ mantener | |

---

## Potencia

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| W, watt | watt | 🌍 cotidiana | ✅ mantener | |
| hp, horsepower | horsepower | 🇺🇸 EEUU | ✅ mantener | |
| BTU/h | BTU per hour | 🏭 industrial | ❌ bloquear | Expresión compuesta; BTU en `blocked` |

---

## Presión

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| Pa, pascal | pascal | 🌍 cotidiana | ✅ mantener | |
| bar | bar | 🌍 cotidiana | ✅ mantener | |
| atm | atmosphere | 🔬 científica | ✅ mantener | |
| mmHg, torr | millimeter of mercury | 💊 médica | ✅ mantener | |
| psi | pounds per square inch | 🇺🇸 EEUU | ✅ mantener | |

---

## Electricidad / Electromagnetismo

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| A, ampere | ampere | 🌍 cotidiana | ✅ mantener | sin panel de conversión automático |
| V, volt | volt | 🌍 cotidiana | ✅ mantener | sin panel de conversión automático |
| ohm | ohm | 🌍 cotidiana | ✅ mantener | tokenAlias: `ohms` → ohm; sin panel de conversión automático |
| F, farad | farad | 🔬 científica | ✅ mantener | tokenAlias `f` y `F` → degF; válido en cálculos, sin panel de conversión automático |
| H, henry | henry | 🔬 científica | ✅ mantener | sin panel de conversión automático |
| Wb, weber | weber | 🔬 científica | ✅ mantener | |
| T, tesla | tesla | 🔬 científica | ✅ mantener | sin panel de conversión automático |
| S, siemens | siemens | 🔬 científica | ✅ mantener | tokenAlias: `siemens` → S; sin panel de conversión automático |
| C, coulomb | coulomb | 🔬 científica | ✅ mantener | tokenAlias `c` y `C` → degC; válido en cálculos, sin panel de conversión automático |
| Sv, sievert | sievert | 🔬 científica | ❌ bloquear | En `blocked`. No existe en esta build de math.js |
| Gy, gray | gray | 🔬 científica | ❌ bloquear | En `blocked`. No existe en esta build de math.js |
| Bq, becquerel | becquerel | 🔬 científica | ❌ bloquear | En `blocked`. No existe en esta build de math.js |
| Hz, hertz | hertz | 🌍 cotidiana | ✅ mantener | |
| lm, lumen | lumen | 🔬 científica | ❌ bloquear | En `blocked`. No existe en esta build de math.js |
| lx, lux | lux | 🔬 científica | ❌ bloquear | En `blocked`. No existe en esta build de math.js |
| cd, candela | candela | 🔬 científica | ❌ bloquear | En `blocked`. Sin par de conversión cotidiano |
| mol, mole | mole | 🔬 científica | ✅ mantener | tokenAlias: `moles` → mol; sin panel de conversión automático |

---

## Datos / Informática

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| bit, b | bit | 🌍 cotidiana | ✅ mantener | |
| B | byte | 🌍 cotidiana | ✅ mantener | tokenAlias: `bytes` → B |
| kB | kilobyte | 🌍 cotidiana | ✅ mantener | tokenAlias: `kilobytes` → kB |
| MB | megabyte | 🌍 cotidiana | ✅ mantener | tokenAlias: `megabytes` → MB |
| GB | gigabyte | 🌍 cotidiana | ✅ mantener | tokenAlias: `gigabytes` → GB |
| TB | terabyte | 🌍 cotidiana | ✅ mantener | tokenAlias: `terabytes` → TB |

---

## Velocidad

> **Limitación**: las unidades de velocidad son expresiones compuestas en math.js (`m/s`, `km/h`, `mph`) o no existen en esta build (`knot`, `kn`). No funcionan como fuente de auto-conversión `unit_entry` (el AST requiere `OperatorNode(implicit, [number, SymbolNode])`). Se pueden usar en cálculos pero no generarán conversión automática al escribir "10 m/s".

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| m/s | meters per second | 🌍 cotidiana | ✅ mantener | Solo funciona en cálculos, no como unit_entry |
| km/h | kilometers per hour | 🌍 cotidiana | ✅ mantener | Solo funciona en cálculos, no como unit_entry |
| mph | miles per hour | 🇺🇸 EEUU | ✅ mantener | Solo funciona en cálculos, no como unit_entry |
| knot | knot | 🏭 náutica | — | No existe en esta build de math.js |
| kn | — | — | — | `kn` resuelve a `kN` (kilonewton), no a knot |
| c (velocidad de la luz) | speed of light | 🔬 científica | ⚠ override | tokenAlias `c` y `C` → degC. Usar `lightspeed` para la velocidad de la luz |

---

## Fuerza

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| N, newton | newton | 🌍 cotidiana | ✅ mantener | |
| dyn, dyne | dyne | 🔬 científica | ✅ mantener | |
| lbf | pound-force | 🇺🇸 EEUU | ✅ mantener | tokenAlias: `poundforce` → lbf, `poundforces` → lbf |
| kip | kilopound-force | 🇺🇸 EEUU | ✅ mantener | |
| kgf | kilogram-force | 🌍 cotidiana | ✅ mantener | |

---

## Notas de diseño

- `tokenAliases` `c`/`C` → `degC` y `f`/`F` → `degF` hacen que tanto mayúsculas como minúsculas se interpreten como temperatura. `10C` y `10c` son celsius; `10F` y `10f` son fahrenheit. Las unidades físicas coulomb y farad siguen disponibles via `resolveUnitToken` pero sin panel de conversión automático.
- Las unidades `A`, `V`, `ohm`, `F`, `H`, `C`, `T`, `S`, `mol` no generan panel de conversión automático porque sus únicos targets serían prefijos (mA, mV, kohm…) sin valor informativo. Siguen siendo válidas en cálculos y expresiones.
- Las unidades `blocked` se definen en `unit-config.json` y se rechazan en `resolveUnitToken()` antes de que math.js las interprete, evitando resultados desconcertantes.
- Los `displayNames` solo afectan a la presentación en la UI y al texto copiado al portapapeles; la evaluación interna siempre usa los símbolos canónicos de math.js.
- `tablespoon` y `teaspoon` son los símbolos canónicos en esta build de math.js. `tsp` y `tbsp` no existen como símbolos directos — deben ir como `tokenAliases`.
- Las unidades de velocidad (`knot`, `mph`, `km/h`, `m/s`) son expresiones compuestas y no funcionan como fuente `unit_entry` para auto-conversión. Se pueden usar en cálculos pero no generan panel de conversión automático.
- `Bq`, `Sv`, `Gy`, `lm`, `lx` no existen en esta build de math.js (no hay precomputed symbol). Están en `blocked` para que den un error claro en vez de comportamiento inesperado.
- La cadena de energía práctica es J → kJ → Wh (electricidad). BTU y kcal no están disponibles en esta build.

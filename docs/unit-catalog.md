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
| quart, qt | quart | 🇺🇸 EEUU | ✅ mantener | |
| pint, pt | pint | 🇺🇸 EEUU | ✅ mantener | |
| cup | cup | 🇺🇸 EEUU | ✅ mantener | |
| floz | fluid ounce | 🇺🇸 EEUU | ✅ mantener | |
| tbsp, tablespoon | tablespoon | 🇺🇸 EEUU | ✅ mantener | |
| tsp, teaspoon | teaspoon | 🇺🇸 EEUU | ✅ mantener | |
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
| day | day | 🌍 cotidiana | ✅ mantener | |
| week | week | 🌍 cotidiana | ✅ mantener | |
| month | month | 🌍 cotidiana | ✅ mantener | |
| year | year | 🌍 cotidiana | ✅ mantener | |
| decade | decade | 🌍 cotidiana | ✅ mantener | |
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
| BTU, btu | British thermal unit | 🇺🇸 EEUU | ✅ mantener | |
| cal, calorie | calorie | 🌍 cotidiana | ✅ mantener | |
| kcal | kilocalorie | 🌍 cotidiana | ✅ mantener | |
| erg | erg | 🔬 científica | ✅ mantener | |
| planckEnergy | Planck energy | 🔬 científica | ✅ mantener | |

---

## Potencia

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| W, watt | watt | 🌍 cotidiana | ✅ mantener | |
| hp, horsepower | horsepower | 🇺🇸 EEUU | ✅ mantener | |
| BTU/h | BTU per hour | 🏭 industrial | ✅ mantener | |

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
| A, ampere | ampere | 🌍 cotidiana | ✅ mantener | |
| V, volt | volt | 🌍 cotidiana | ✅ mantener | |
| ohm | ohm | 🌍 cotidiana | ✅ mantener | |
| F, farad | farad | 🔬 científica | ✅ mantener | tokenAlias `f` → degF (no afecta `F` mayúscula) |
| H, henry | henry | 🔬 científica | ✅ mantener | |
| Wb, weber | weber | 🔬 científica | ✅ mantener | |
| T, tesla | tesla | 🔬 científica | ✅ mantener | |
| S, siemens | siemens | 🔬 científica | ✅ mantener | |
| C, coulomb | coulomb | 🔬 científica | ✅ mantener | tokenAlias `c` → degC (no afecta `C` mayúscula) |
| Sv, sievert | sievert | 🔬 científica | ✅ mantener | |
| Gy, gray | gray | 🔬 científica | ✅ mantener | |
| Bq, becquerel | becquerel | 🔬 científica | ✅ mantener | |
| Hz, hertz | hertz | 🌍 cotidiana | ✅ mantener | |
| lm, lumen | lumen | 🔬 científica | ✅ mantener | |
| lx, lux | lux | 🔬 científica | ✅ mantener | |
| cd, candela | candela | 🔬 científica | ✅ mantener | |
| mol, mole | mole | 🔬 científica | ✅ mantener | |

---

## Datos / Informática

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| bit, b | bit | 🌍 cotidiana | ✅ mantener | |
| byte, B | byte | 🌍 cotidiana | ✅ mantener | |
| KB, kB | kilobyte | 🌍 cotidiana | ✅ mantener | |
| MB | megabyte | 🌍 cotidiana | ✅ mantener | |
| GB | gigabyte | 🌍 cotidiana | ✅ mantener | |
| TB | terabyte | 🌍 cotidiana | ✅ mantener | |

---

## Velocidad

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| m/s | meters per second | 🌍 cotidiana | ✅ mantener | |
| km/h, kph | kilometers per hour | 🌍 cotidiana | ✅ mantener | |
| mph | miles per hour | 🇺🇸 EEUU | ✅ mantener | |
| knot, kn | knot | 🏭 náutica | ✅ mantener | |
| c (velocidad de la luz) | speed of light | 🔬 científica | ⚠ override | tokenAlias `c` → degC. `C` mayúscula sigue siendo coulomb. Usar `lightspeed` para la velocidad de la luz |

---

## Fuerza

| Símbolo | Long name | Tipo | Decisión | Notas |
|---------|-----------|------|----------|-------|
| N, newton | newton | 🌍 cotidiana | ✅ mantener | |
| dyn, dyne | dyne | 🔬 científica | ✅ mantener | |
| lbf | pound-force | 🇺🇸 EEUU | ✅ mantener | |
| kip | kilopound-force | 🇺🇸 EEUU | ✅ mantener | |
| kgf | kilogram-force | 🌍 cotidiana | ✅ mantener | |

---

## Notas de diseño

- `tokenAliases` `c` → `degC` y `f` → `degF` son aliases de **token lowercase**. Math.js distingue mayúsculas: `C` (coulomb) y `F` (farad) siguen funcionando porque `resolveUnitToken` solo aplica el override al token exacto `c`/`f`.
- Las unidades `blocked` se definen en `unit-config.json` y se rechazan en `resolveUnitToken()` antes de que math.js las interprete, evitando resultados desconcertantes.
- Los `displayNames` solo afectan a la presentación en la UI y al texto copiado al portapapeles; la evaluación interna siempre usa los símbolos canónicos de math.js.

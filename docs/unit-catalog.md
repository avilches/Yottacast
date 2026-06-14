# Catalogo de unidades -- Conversor y Calculadora

Este fichero es solo el **catalogo de unidades**: que unidades acepta la calculadora por categoria
y que unidades estan bloqueadas y por que.

El **comportamiento** de la calculadora (resolucion de tokens, conversion automatica, normalizacion
de tiempo/datos, ambiguedad, divisas, display/long names, errores, formateo) se documenta en
`docs/search-calculator.md`. Este fichero no describe comportamiento: solo inventaria unidades.

La **fuente de verdad** del catalogo es `Yottacast.Core/Search/Calculator/unit-config.json` (aliases,
bloqueos, targets) mas el registry de math.js precomputado en `mathjs-precomputed.json`. Las tablas de
abajo son una referencia legible; ante cualquier discrepancia, gana el codigo.

---

## 1. Unidades bloqueadas

Las unidades bloqueadas se rechazan antes de que math.js las interprete. El objetivo es evitar
resultados confusos para unidades demasiado oscuras, inexistentes en esta build de math.js, o
con duracion variable. El mecanismo de bloqueo y su efecto se describen en `docs/search-calculator.md`
(seccion de resolucion de unidades, paso "token bloqueado").

| Categoria | Unidades bloqueadas | Motivo |
|-----------|-------------------|--------|
| Topografia/agrimensura | `li`, `ch`, `rd`, `link`, `links`, `rod`, `rods`, `chain`, `chains` | Oscuras para usuarios generales |
| Area topografica | `sqmil`, `sqrd`, `sqch` | Oscuras para usuarios generales |
| Volumen farmaceutico/historico | `obl`, `lt`, `gi`, `cp`, `gtt`, `gtts`, `fldr`, `fluiddram`, `fluiddrams`, `minim`, `minims`, `gill`, `gills` | Oscuras o historicas |
| Energia | `BTU` | Cadena practica preferida: J -> kJ -> Wh |
| Electromagnetismo | `cd` (candela) | Sin par de conversion cotidiano |
| No existentes en esta build | `Bq` (becquerel), `Sv` (sievert), `Gy` (gray), `lm` (lumen), `lx` (lux) | No estan precomputados; bloquear da un error claro en vez de comportamiento inesperado |
| Tiempo variable | `month`, `months` | Duracion variable (28-31 dias) produce conversiones confusas |
| Funcion math.js | `gcd` | Colisiona con la funcion `gcd()` de math.js |

> **Verificar en:**
> - `unit-config.json`, seccion `blocked` -- lista completa de tokens bloqueados
> - `mathjs-helpers.js`, funcion `resolveUnitToken()` -- rechazo de tokens bloqueados

---

## 2. Inventario de unidades aceptadas por categoria

A continuacion se listan las unidades base que el sistema acepta. No se incluyen combinaciones
prefijo+unidad (como `km`, `mA`, `kB`), que son validas automaticamente si la unidad base lo es y el
prefijo es valido en math.js. La columna "Canonico" indica el simbolo interno de math.js al que se
resuelve la entrada; el mecanismo de resolucion de aliases vive en `docs/search-calculator.md`.

### Temperatura

| Entrada aceptada | Canonico |
|-------------------|---------|
| degC, celsius, c, C, °c, ºc | degC |
| degF, fahrenheit, f, F, °f, ºf | degF |
| K, kelvin | K |
| degR, rankine | degR |

### Longitud / Distancia

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| m, meter, meters | m | |
| in, inch, inches | in | |
| ft, foot, feet | ft | |
| yard, yards | yard | |
| mi, mile, miles | mi | |
| nmi, nauticalMile | nmi | |
| angstrom | angstrom | |
| mil | mil | Milesima de pulgada |
| fathom | fathom | |
| parsec | parsec | |
| ly, lightyear | ly | |
| AU, astronomicalUnit | AU | |
| a0, bohr | a0 | |
| planckLength | planckLength | |

### Masa / Peso

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| g, gram, grams | g | |
| t, tonne, tonnes | t | |
| lb, lbs, pound, pounds | lb | |
| oz, ounce, ounces | oz | |
| ton | ton | Short ton (US) |
| longton | longton | |
| cwt, hundredweight | cwt | |
| gr, grain | gr | |
| ct, carat | ct | |
| stone | stone | |
| planckMass | planckMass | |
| u, atomicMass | u | |
| electronMass | electronMass | |

### Volumen

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| L, l, liter, litre, liters, litres | L | |
| mL, ml | mL | |
| gallon, gal, gallons | gallon | |
| quart, quarts | quart | |
| pint, pints | pint | |
| cup, cups | cup | |
| floz | floz | |
| tablespoon, tbsp, tablespoons | tablespoon | `tablespoon` es el canonico; `tbsp` es alias |
| teaspoon, tsp, teaspoons | teaspoon | `teaspoon` es el canonico; `tsp` es alias |
| cc | cc | Sinonimo de mL |

### Tiempo

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| s, seconds | s | |
| minute, minutes, min | minute | `min` se evalua como `minute` para evitar colision con `math.min()` |
| h, hours, H | h | |
| day, days, d, D | day | |
| week, weeks | week | |
| year, years | year | |
| decade, decades | decade | |
| century | century | |
| millennium | millennium | |
| ms, milliseconds | ms | |
| planckTime | planckTime | |

### Area

| Entrada aceptada | Canonico |
|-------------------|---------|
| m2 | m2 |
| hectare, ha, hectares | hectare |
| acre, acres | acre |
| sqin | sqin |
| sqft | sqft |
| sqyd | sqyd |
| sqmi | sqmi |

### Angulo

| Entrada aceptada | Canonico |
|-------------------|---------|
| rad, radian, radians | rad |
| deg, degree, degrees | deg |
| grad, gradian, gradians | grad |
| cycle | cycle |
| arcmin, arcminute, arcminutes | arcmin |
| arcsec, arcsecond, arcseconds | arcsec |

### Energia

| Entrada aceptada | Canonico |
|-------------------|---------|
| J, joule | J |
| eV, electronvolt | eV |
| Wh | Wh |
| erg | erg |
| planckEnergy | planckEnergy |

Nota: `calorie`, `kcal` y `BTU` no estan disponibles. `BTU` esta explicitamente bloqueado (ver seccion 1).
`calorie`/`kcal` no existen como simbolos en esta build de math.js.

### Potencia

| Entrada aceptada | Canonico |
|-------------------|---------|
| W, watt, watts, w | W |
| hp, horsepower, horsepowers | hp |

### Presion

| Entrada aceptada | Canonico |
|-------------------|---------|
| Pa, pascal, pascals | Pa |
| bar | bar |
| atm, atmosphere, atmospheres | atm |
| mmHg, torr | mmHg |
| psi | psi |

### Electricidad / Electromagnetismo

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| A, ampere, amperes | A | |
| V, volt, volts, v | V | |
| ohm, ohms | ohm | |
| F, farad | F | `F` resuelve a `degF` por alias; farad accesible via nombre completo |
| H, henry, henrys | H | `H` resuelve a `h` (hour) por alias |
| Wb, weber | Wb | |
| T, tesla, teslas | T | |
| S, siemens | S | |
| C, coulomb | C | `C` resuelve a `degC` por alias; coulomb accesible via nombre completo |
| Hz | Hz | |
| mol, moles | mol | |

### Datos / Informatica

| Entrada aceptada | Canonico |
|-------------------|---------|
| bit, b | bit |
| B, bytes | B |
| kB, kb, kilobytes | kB |
| MB, megabytes | MB |
| GB, gigabytes | GB |
| TB, terabytes | TB |

Tambien soporta prefijos binarios: KiB, MiB, GiB, TiB, PiB, EiB, ZiB, YiB.

### Velocidad

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| kmh, kmph, kph | kmh | Unidad personalizada: `1000/3600 m/s` |
| mph | mph | Unidad personalizada: `1609.344/3600 m/s` |
| m/s, km/h, mi/h, ft/s, etc. | (compuesto) | Funcionan en conversiones explicitas y compuestas |

Nota: `knot`/`kn` no existe. `kn` resuelve a `kN` (kilonewton) por el sistema de prefijos de math.js.

### Fuerza

| Entrada aceptada | Canonico |
|-------------------|---------|
| N, newton, newtons | N |
| dyn, dyne | dyn |
| lbf, poundforce, poundforces | lbf |
| kip | kip |
| kgf | kgf |

### Frecuencia / Rotacion

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| Hz, hertz | Hz | |
| rpm | rpm | Unidad personalizada: `1/60 Hz` |

### Tasas de datos

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| bps | bps | bit/s |
| kbps | kbps | 1000 bps |
| Mbps | Mbps | 1000 kbps |
| Gbps | Gbps | 1000 Mbps |
| Tbps | Tbps | 1000 Gbps |

> **Verificar en:**
> - `unit-config.json` -- secciones `tokenAliases`, `inputAliases`, `blocked` (fuente de verdad de aliases y bloqueos)
> - `mathjs-helpers.js`, primeras lineas -- definicion de unidades personalizadas (kmh, mph, rpm, bps, etc.)
> - `mathjs-precomputed.json` -- mapa de simbolos canonicos de math.js

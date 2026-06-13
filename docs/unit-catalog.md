# Catalogo de unidades -- Conversor y Calculadora

Este documento describe **que debe hacer** el sistema de unidades de Yottacast y **por que**,
junto con invariantes verificables. El codigo fuente es la fuente de verdad ante cualquier discrepancia.

---

## 1. Proposito general

Cuando el usuario escribe una expresion en el launcher (p.ej. `10 km`, `5 lb to kg`, `2+3`),
el sistema debe:

1. Reconocer si es una conversion de unidades, una expresion matematica o una entrada de moneda.
2. Mostrar un resultado instantaneo con la conversion o calculo.
3. Permitir copiar el resultado al portapapeles al activar el item.

El motor de evaluacion es math.js ejecutado dentro de Jint (interprete JS en .NET). La
inicializacion ocurre en un hilo de fondo para no bloquear el arranque de la app.

> **Verificar en:**
> - `Yottacast.Core/Search/Calculator/MathJsEngine.cs` -- clase `MathJsEngine`, metodo `Initialize()` y `Evaluate()`
> - `Yottacast.Core/Search/Calculator/CalculatorSearch.cs` -- clase `CalculatorSearch`, metodo `Search()`

---

## 2. Ciclo de vida de una expresion

El procesado de una expresion sigue siempre estas fases, en orden:

| Fase | Que ocurre | Invariante |
|------|-----------|------------|
| **Input aliases** | Se reemplazan caracteres especiales antes del parseo (p.ej. `°c` -> `degC`, `ºf` -> `degF`). | Siempre ocurre antes de que math.js vea la expresion. |
| **Parseo AST** | math.js parsea la expresion en un arbol sintactico. | Las definiciones de funciones (`FunctionAssignmentNode`) se descartan silenciosamente (retornan null). |
| **Normalizacion** | Se recorre el AST: se resuelven tokens de unidad (case, aliases, bloqueos), monedas a mayusculas, funciones a su casing canonico. | Cada `SymbolNode` pasa por `resolveUnitToken()`. Si la unidad esta en `blocked`, se trata como no reconocida. |
| **Clasificacion** | Se determina el tipo: `calculation`, `unit_entry`, `simple_conversion`, `complex_conversion`. | Una `unit_entry` solo se genera si existe un `defaultTarget` para la unidad resuelta. |
| **Evaluacion** | Se ejecuta `math.evaluate()` con la expresion normalizada. | Las tasas de cambio de monedas se actualizan en cada llamada desde `ICurrencyRateProvider.CachedRates`. |
| **Formateo** | El resultado numerico se formatea con precision inteligente (2 decimales para |n| >= 1, 3 cifras significativas para |n| < 1). | Los enteros nunca muestran decimales. |

> **Verificar en:**
> - `MathJsEngine.NormalizeExpressionCore()` -- fases input aliases, parseo, normalizacion, clasificacion
> - `MathJsEngine.EvaluateSimple()` y `EvaluateComplex()` -- fase de evaluacion
> - `mathjs-helpers.js`, funcion `smartFormat()` -- fase de formateo

---

## 3. Resolucion de tokens de unidad

Cuando el usuario escribe un token (p.ej. `c`, `km`, `mS`), el sistema debe resolverlo a su
forma canonica en math.js. Las reglas son, en orden de prioridad:

| Prioridad | Regla | Ejemplo |
|-----------|-------|---------|
| 1 | Si el token (lowercase) esta en `blocked`, se rechaza (retorna null). | `BTU`, `month`, `obl` -> rechazados |
| 2 | Si existe un `tokenAlias` exacto (case-sensitive), se usa. | `c` -> `degC`, `F` -> `degF`, `tsp` -> `teaspoon` |
| 3 | Para tokens multi-caracter, se prueba tambien la version lowercase del alias. | `Hour` -> `h`, `MILES` -> `mi` |
| 4 | Si el token es ambiguo (multiples canonicos en math.js), se aplican reglas de desambiguacion. | `mS` -> `ms` (via `forceAmbiguous`), `mm` -> `mm` (via `ambiguityOverrides`) |
| 5 | Si el token coincide exactamente con un canonico, se usa sin ambiguedad. | `Pa` -> `Pa`, `Hz` -> `Hz` |
| 6 | Busqueda por lowercase en `_unitSymbols` (mapa precomputado). | `kj` -> `kJ` |
| 7 | Si nada coincide, retorna null (token no reconocido). | |

**Invariantes de la resolucion:**

- Las letras sueltas `c`/`C` siempre resuelven a `degC` (celsius); `f`/`F` siempre resuelven a `degF` (fahrenheit). Los simbolos fisicos coulomb (`C`) y farad (`F`) quedan accesibles solo escribiendo el nombre completo o en expresiones donde ya no pasan por `tokenAlias`.
- Los tokens de un solo caracter son case-sensitive en el mapa de overrides. Los tokens de mas de un caracter son case-insensitive.
- `v` y `w` (minusculas) resuelven a `V` (volt) y `W` (watt) respectivamente.
- `d` y `D` resuelven a `day`; `H` resuelve a `h` (hour).
- El alias `min` se trata de forma especial: en contexto de evaluacion se reemplaza por `minute` para evitar colision con la funcion `math.min()`.

> **Verificar en:**
> - `mathjs-helpers.js`, funcion `resolveUnitToken()` -- logica completa de resolucion
> - `unit-config.json`, secciones `tokenAliases`, `blocked`, `ambiguityOverrides`, `forceAmbiguous`
> - `unit-config.json`, seccion `evalSafeAliases` -- caso `min` -> `minute`

---

## 4. Unidades bloqueadas

Las unidades bloqueadas se rechazan antes de que math.js las interprete. El objetivo es evitar
resultados confusos para unidades demasiado oscuras, inexistentes en esta build de math.js, o
con duracion variable.

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

**Invariante:** El usuario nunca ve un resultado para una unidad bloqueada. Si escribe `10 BTU` o `5 months`, el sistema no genera ningun item de resultado.

> **Verificar en:**
> - `unit-config.json`, seccion `blocked`
> - `mathjs-helpers.js`, funcion `resolveUnitToken()` -- primera linea: `if (_blockedUnits.has(name.toLowerCase())) return null;`

---

## 5. Conversion automatica (unit_entry)

Cuando el usuario escribe solo un valor con una unidad (p.ej. `10 km`), el sistema busca automaticamente
una unidad destino predeterminada y muestra la conversion sin que el usuario escriba "to".

Esto solo ocurre si existe una entrada en `defaultTargets` para la unidad resuelta.

### 5.1. Pares por defecto (extracto representativo)

| Origen | Destino | Bidireccional |
|--------|---------|:------------:|
| m | ft | Si |
| km | mile | Si |
| cm | in | Si |
| kg | lb | Si |
| g | oz | Si |
| degC | degF | Si |
| L | gallon | Si |
| Pa | psi | -- |
| psi | bar | -- |
| bar | psi | Si |
| J | kJ | -- |
| kJ | Wh | -- |
| W | kW | -- |
| kW | hp | Si |
| B | kB | -- |
| kB | MB | -- |
| MB | GB | -- |
| rad | deg | Si |
| hectare | acre | Si |
| kmh | mph | Si |
| Hz | rpm | Si |
| bps | B / s | -- |

La tabla completa vive en `unit-config.json` seccion `defaultTargets`.

### 5.2. Unidades sin conversion automatica

Algunas unidades son validas en calculos y conversiones explicitas (`X to Y`) pero **no generan panel de conversion automatica** porque sus targets solo serian prefijos SI sin valor informativo:

`A` (ampere), `V` (volt), `ohm`, `F` (farad), `H` (henry), `C` (coulomb), `T` (tesla), `S` (siemens), `mol` (mole).

Estas unidades no tienen entrada en `defaultTargets`, por lo que `10 V` no genera resultado, pero `10 V to mV` si funciona.

### 5.3. Unidades compuestas (velocidad, tasas de datos)

Las unidades compuestas como `km/h`, `m/s`, `mph`, `bps` se registran como unidades simples personalizadas
en math.js (`math.createUnit`) con su definicion en terminos de unidades base. Esto permite:

- Que `10 kmh` genere conversion automatica a `mph` via `defaultTargets`.
- Que `10 km/h to mi/h` funcione como conversion explicita.
- Que los aliases `kmph`, `kph` resuelvan a `kmh`.

**Invariante:** `knot`/`kn` no existe en esta build. `kn` resuelve a `kN` (kilonewton) por el sistema de prefijos de math.js.

> **Verificar en:**
> - `unit-config.json`, seccion `defaultTargets`
> - `mathjs-helpers.js`, funcion `findDefaultTarget()` -- logica de busqueda de destino
> - `mathjs-helpers.js`, lineas 4-8 -- definicion de `kmh`, `mph`, `rpm`
> - `mathjs-helpers.js`, funcion `_isCompoundUnitEntry()` -- deteccion de patron AST `numero * unidad / unidad`

---

## 6. Normalizacion (descomposicion temporal y de datos)

Para ciertas unidades, el sistema descompone el resultado en componentes legibles en vez de mostrar
un solo numero grande. Existen dos modos:

| Modo | Categorias | Comportamiento | Ejemplo |
|------|-----------|---------------|---------|
| Descomposicion multi-componente | Tiempo (ms, s, minute, h, day, week, year) | Hasta 3 componentes del mas grande al mas pequeno | `38000 s` -> `10 h 33 min 20 s` |
| Mejor unidad unica | Datos (B, kB, MB, GB, TB) | La unidad mas grande donde el valor >= 1 | `1500 MB` -> `1.5 GB` |

**Invariantes:**

- La normalizacion solo se aplica a `unit_entry` (conversion automatica), nunca a conversiones explicitas.
- Solo produce resultado cuando la descomposicion es "interesante" (difiere de la unidad de entrada).
- Unidades con un `defaultTarget` explicito (como `decade` -> `year`, `week` -> `day`) se excluyen del fallback dimensional: su conversion predeterminada tiene prioridad.
- Los prefijos SI de unidades de tiempo o datos (ks, Ms, PB, EB...) se cubren por compatibilidad dimensional con la cadena base.

> **Verificar en:**
> - `mathjs-helpers.js`, variable `_normalizeChains` -- definicion de cadenas
> - `mathjs-helpers.js`, funciones `isNormalizableUnit()` y `computeNormalization()`
> - `MathJsEngine.TryNormalize()` -- integracion en el flujo de evaluacion
> - `unit-config.json`, seccion `normalizeUnits`

---

## 7. Presentacion y nombres largos

### 7.1. Display names

Los `displayNames` solo afectan la presentacion en la UI y el texto copiado al portapapeles.
La evaluacion interna siempre usa los simbolos canonicos de math.js.

| Canonico | Se muestra como |
|----------|----------------|
| degC | C |
| degF | F |
| degR | R |
| minute | min |
| hectare | ha |

Para unidades compuestas (p.ej. `mi / minute`), el lookup se aplica a cada componente por separado,
mostrando `mi/min` en vez de `mi / minute`.

### 7.2. Nombres largos y pluralizacion

Cada unidad puede tener un nombre largo (p.ej. `ft` -> `foot`, `lb` -> `pound`) que se muestra
como texto secundario junto al resultado. El sistema intenta derivar el nombre largo de tres formas:

1. Busqueda explicita en `longNames` de `unit-config.json`.
2. Para unidades compuestas (`km / h`), composicion automatica: `kilometer per hour`.
3. Derivacion automatica via el grupo de prefijos LONG de math.js (p.ej. `km` -> `kilometer`).

La pluralizacion sigue reglas simples:

| Regla | Ejemplo |
|-------|---------|
| Valor = 1 o -1: singular | `1 foot`, `1 meter` |
| `foot` -> `feet`, `inch` -> `inches` | Irregulares |
| Terminados en `hertz`: invariantes | `10 kilohertz` |
| Terminados en `s` o `heit`: invariantes | `10 fahrenheit` |
| Forma `X per Y`: solo se pluraliza X | `10 kilometers per hour` |
| Resto: se anade `s` | `10 meters`, `10 pounds` |

> **Verificar en:**
> - `unit-config.json`, secciones `displayNames` y `longNames`
> - `MathJsEngine.DisplayUnit()` -- aplicacion de display names, incluyendo compuestos
> - `MathJsEngine.GetUnitLongName()` -- derivacion de nombre largo
> - `UnitPluralizer.cs` -- reglas de pluralizacion

---

## 8. Ambiguedad de unidades

Cuando un token coincide con multiples simbolos canonicos en math.js (p.ej. `mS` podria ser
`ms` = milisegundo o `mS` = millisiemens), el sistema muestra un aviso de ambiguedad al usuario.

El tratamiento se configura en dos mapas:

| Mapa | Clave | Uso |
|------|-------|-----|
| `ambiguityOverrides` | Token en minusculas | Define el canonico preferido cuando el token no es un match exacto. P.ej. `mm` -> `mm` (milimetro, no megametro). |
| `forceAmbiguous` | Token case-sensitive | Fuerza la ambiguedad incluso cuando el token coincide exactamente con un canonico. P.ej. `mS` -> `ms` con aviso de que podria ser millisiemens. |

**Invariantes:**

- El aviso de ambiguedad dice "Maybe you meant X or Y?" mostrando las alternativas que NO fueron seleccionadas.
- Si todos los candidatos tienen el mismo `longName` (son sinonimos, como `l` y `L` = litro), no se marca como ambiguo.
- Cada token ambiguo se reporta como maximo una vez por expresion (deduplicacion por lowercase).

> **Verificar en:**
> - `unit-config.json`, secciones `ambiguityOverrides` y `forceAmbiguous`
> - `mathjs-helpers.js`, funcion `resolveUnitToken()` -- logica de ambiguedad
> - `CalculatorSearch.BuildHints()` -- formato del mensaje al usuario

---

## 9. Conversion de monedas

Las monedas se tratan como unidades de math.js con USD como base. El sistema:

1. Reconoce codigos ISO en mayusculas (EUR, GBP, JPY...) a traves de `ICurrencyRateProvider`.
2. Registra cada moneda como `math.createUnit(name, { definition: (1/rate) + ' USD' })`.
3. Actualiza las tasas en cada evaluacion desde el cache del proveedor.
4. Por defecto, si el usuario escribe una moneda sola (p.ej. `100 EUR`), sugiere conversion a USD y viceversa.

**Invariante:** Las tasas registradas en el motor JS se rastrean para evitar llamadas redundantes a `math.createUnit` que podrian corromper el estado de math.js.

> **Verificar en:**
> - `mathjs-helpers.js`, funcion `registerCurrency()` y variable `_defaultCurrencyPair`
> - `MathJsEngine.Evaluate()` -- registro de monedas antes de la evaluacion
> - `ICurrencyRateProvider.cs` -- interfaz del proveedor

---

## 10. Manejo de errores

Cuando la evaluacion falla, el sistema clasifica el error para dar feedback apropiado:

| Tipo de error | Comportamiento visible |
|--------------|----------------------|
| `unknown_symbol` | No se muestra nada (el texto no es una expresion valida). |
| `incompatible_units` | Se muestra un hint informativo: "Incompatible units" o el mensaje de math.js. |
| `syntax` | No se muestra nada. |
| `other` | No se muestra nada. |

**Invariante:** Solo los errores de tipo `incompatible_units` generan un hint visible. Todos los demas tipos producen silencio (lista vacia de resultados), para no molestar al usuario mientras escribe texto que no es una expresion.

> **Verificar en:**
> - `mathjs-helpers.js`, funcion `classifyError()`
> - `CalculatorSearch.Search()` -- el `switch` sobre `ErrorResult`
> - `CalculatorSearch.BuildErrorHint()` -- formato del mensaje

---

## 11. Inventario de unidades aceptadas por categoria

A continuacion se listan las unidades base que el sistema acepta (no incluye combinaciones
prefijo+unidad como `km`, `mA`, etc., que son validas automaticamente si la unidad base lo es).

### Temperatura

| Entrada aceptada | Canonico | Aliases configurados |
|-------------------|---------|---------------------|
| degC, celsius, c, C, grados celsius | degC | inputAliases: `°c`/`ºc` -> degC; tokenAliases: `c`/`C` -> degC |
| degF, fahrenheit, f, F | degF | inputAliases: `°f`/`ºf` -> degF; tokenAliases: `f`/`F` -> degF |
| K, kelvin | K | -- |
| degR, rankine | degR | -- |

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
| tablespoon, tbsp, tablespoons | tablespoon | `tablespoon` es el simbolo canonico en math.js; `tbsp` es un tokenAlias |
| teaspoon, tsp, teaspoons | teaspoon | `teaspoon` es el simbolo canonico en math.js; `tsp` es un tokenAlias |
| cc | cc | Sinonimo de mL |

### Tiempo

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| s, seconds | s | |
| minute, minutes, min | minute | `min` usa `evalSafeAlias` para evitar colision con `math.min()` |
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

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| m2 | m2 | |
| hectare, ha, hectares | hectare | |
| acre, acres | acre | |
| sqin | sqin | |
| sqft | sqft | |
| sqyd | sqyd | |
| sqmi | sqmi | |

### Angulo

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| rad, radian, radians | rad | |
| deg, degree, degrees | deg | |
| grad, gradian, gradians | grad | |
| cycle | cycle | |
| arcmin, arcminute, arcminutes | arcmin | |
| arcsec, arcsecond, arcseconds | arcsec | |

### Energia

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| J, joule | J | |
| eV, electronvolt | eV | |
| Wh | Wh | |
| erg | erg | |
| planckEnergy | planckEnergy | |

Nota: `calorie`, `kcal` y `BTU` no estan disponibles. `BTU` esta explicitamente bloqueado. `calorie`/`kcal` no existen como simbolos en esta build de math.js.

### Potencia

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| W, watt, watts, w | W | |
| hp, horsepower, horsepowers | hp | |

### Presion

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| Pa, pascal, pascals | Pa | |
| bar | bar | |
| atm, atmosphere, atmospheres | atm | |
| mmHg, torr | mmHg | |
| psi | psi | |

### Electricidad / Electromagnetismo

| Entrada aceptada | Canonico | Sin conversion automatica | Notas |
|-------------------|---------|:------------------------:|-------|
| A, ampere, amperes | A | Si | |
| V, volt, volts, v | V | Si | |
| ohm, ohms | ohm | Si | |
| F, farad | F | Si | `F` resuelve a `degF` por tokenAlias; farad accesible via nombre completo |
| H, henry, henrys | H | Si | `H` resuelve a `h` (hour) por tokenAlias |
| Wb, weber | Wb | -- | |
| T, tesla, teslas | T | Si | |
| S, siemens | S | Si | |
| C, coulomb | C | Si | `C` resuelve a `degC` por tokenAlias; coulomb accesible via nombre completo |
| Hz | Hz | -- | Tiene defaultTarget: Hz <-> rpm |
| mol, moles | mol | Si | |

### Datos / Informatica

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| bit, b | bit | |
| B, bytes | B | |
| kB, kb, kilobytes | kB | |
| MB, megabytes | MB | |
| GB, gigabytes | GB | |
| TB, terabytes | TB | |

Tambien soporta prefijos binarios: KiB, MiB, GiB, TiB, PiB, EiB, ZiB, YiB (via `ambiguityOverrides`).

### Velocidad

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| kmh, kmph, kph | kmh | Unidad personalizada: `1000/3600 m/s`. Conversion automatica a mph. |
| mph | mph | Unidad personalizada: `1609.344/3600 m/s`. Conversion automatica a kmh. |
| m/s, km/h, mi/h, ft/s, etc. | (compuesto) | Funcionan en conversiones explicitas y via `defaultTargets` compuestos. |

Nota: `knot`/`kn` no existe. `kn` resuelve a `kN` (kilonewton).

### Fuerza

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| N, newton, newtons | N | |
| dyn, dyne | dyn | |
| lbf, poundforce, poundforces | lbf | |
| kip | kip | |
| kgf | kgf | |

### Frecuencia / Rotacion

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| Hz, hertz | Hz | Conversion automatica a rpm |
| rpm | rpm | Unidad personalizada: `1/60 Hz`. Conversion automatica a Hz. |

### Tasas de datos

| Entrada aceptada | Canonico | Notas |
|-------------------|---------|-------|
| bps | bps | bit/s |
| kbps | kbps | 1000 bps |
| Mbps | Mbps | 1000 kbps |
| Gbps | Gbps | 1000 Mbps |
| Tbps | Tbps | 1000 Gbps |

> **Verificar en:**
> - `unit-config.json` -- todas las secciones: `tokenAliases`, `inputAliases`, `blocked`, `defaultTargets`
> - `mathjs-helpers.js`, lineas 1-15 -- definicion de unidades personalizadas (kmh, mph, rpm, bps, etc.)
> - `mathjs-precomputed.json` -- mapa de simbolos precomputados de math.js

---

## 12. Archivo de configuracion

Toda la configuracion de unidades vive en un unico archivo JSON embebido como recurso en el assembly:

**`Yottacast.Core/Search/Calculator/unit-config.json`**

| Seccion | Proposito |
|---------|----------|
| `inputAliases` | Reemplazos de caracteres especiales antes del parseo (°c, ºf, etc.) |
| `tokenAliases` | Mapeo de tokens a su forma canonica (case-sensitive para 1 caracter, insensitive para el resto) |
| `evalSafeAliases` | Tokens que colisionan con funciones de math.js (`min` -> `minute`) |
| `displayNames` | Nombres para mostrar en la UI (no afectan la evaluacion) |
| `longNames` | Nombres largos explicitos para unidades que math.js no puede derivar automaticamente |
| `defaultTargets` | Unidad destino por defecto para conversion automatica |
| `defaultPairs` | Pares dimensionales de fallback (SI <-> Imperial) para busqueda por compatibilidad |
| `forceAmbiguous` | Tokens canonicos exactos que deben mostrarse como ambiguos (case-sensitive) |
| `ambiguityOverrides` | Canonico preferido para tokens ambiguos no exactos (clave en minusculas) |
| `normalizeUnits` | Unidades que activan el comportamiento de descomposicion/normalizacion |
| `blocked` | Unidades rechazadas antes de llegar a math.js |

> **Verificar en:**
> - `unit-config.json` -- el archivo completo
> - `mathjs-helpers.js`, funcion `loadAliasData()` -- como se carga cada seccion
> - `MathJsEngine.Initialize()` -- inyeccion del JSON en el motor JS

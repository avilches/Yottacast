# Calculadora y conversor de unidades

Implementado como `IInstantSearchSource`: `CalculatorSearch` (`Yottacast.Core/Search/Calculator/CalculatorSearch.cs`). Maneja tanto expresiones matemáticas como conversiones de unidades.

## Motor: MathJsEngine

`MathJsEngine` (`Yottacast.Core/Search/Calculator/MathJsEngine.cs`) — singleton que carga math.js embebido en la DLL (embedded resource en `Yottacast.Core/Search/Calculator/math.min.js`) dentro de un engine Jint 3.x. La inicialización se hace en un background thread; hasta que `WhenReady()` se complete, `Evaluate()` devuelve `ErrorResult` sin bloquear.

**Configuración del engine**: se crea con un límite de recursión (ver `MathJsEngine`).

**WarmUp**: `mathjs-helpers.js` ejecuta `math.createUnit('USD')` al cargarse, lo que dispara la inicialización del sistema de unidades de math.js y actúa como warmup JIT de Jint, de modo que la primera query real del usuario sea instantánea.

**Thread safety**: un `lock (_lock)` protege el acceso al engine durante cada llamada a `Evaluate()`. Es seguro llamarlo desde múltiples hilos.

**Escape de entrada**: antes de pasarla a math.js, la expresión tiene las barras invertidas, comillas simples, saltos de línea y caracteres nulos escapados (ver `Escape()` en `MathJsEngine`).

**Formateo de resultados**: los resultados se formatean con `math.format(r, { precision: 10 })` — 10 dígitos significativos — para evitar ruido de coma flotante como `22.046226218487758`.

**Double-checked null guard**: `Evaluate()` comprueba `_engine == null` antes de adquirir el lock y de nuevo dentro de él. La comprobación exterior garantiza un fast-path sin contención cuando el engine todavía no está listo; la interior garantiza corrección ante una hipotética carrera con `Dispose()`.

**Tipos de resultado de `Evaluate()`**: devuelve un `EvalResult` (`Yottacast.Core/Search/Calculator/EvalResult.cs`) — subtipo `CalcResult` para expresiones aritméticas, `ConversionResult` para conversiones de unidades o divisas, y `ErrorResult` para errores o expresiones inválidas. `ErrorResult` incluye `ErrorKind` (`UnknownSymbol`, `IncompatibleUnits`, `Syntax`, `Other`) y el token problemático cuando aplica.

**Manejo de errores**: los errores de evaluación se clasifican con `classifyError()` (JS, en `mathjs-helpers.js`) en `CalcErrorKind`. Para `UnknownSymbol` e `IncompatibleUnits`, `CalculatorSearch` implementa `ISearchHintProvider` y expone `LastHint` con un mensaje legible. Los errores de sintaxis y otros se descartan silenciosamente.

**math.js descargado en build**: el `.csproj` de Core (`Yottacast.Core/Yottacast.Core.csproj`) incluye un target `DownloadMathJs` que ejecuta `curl` si el fichero no existe. El fichero se excluye del repositorio (`.gitignore`). El primer `dotnet build` lo descarga automáticamente.

**Gotcha — versión de math.js incompatible con Jint**: versiones recientes de math.js lanzan "Assignment to constant variable" dentro de Jint 3.x al ejecutar `math.evaluate`. La versión de math.js embebida está fijada; ver el target `DownloadMathJs` del `.csproj`. Si se actualiza Jint a una versión con soporte ES2022+, se puede probar una versión más reciente de math.js.

**Gotcha — EmbeddedResource condicional**: el `<EmbeddedResource>` de `math.min.js` tiene `Condition="Exists(...)"`. Si `curl` falla en el build, la compilación termina correctamente pero el recurso no queda embebido. En ese caso la app lanza `InvalidOperationException` en runtime al intentar cargar el stream del recurso.

**Dispose**: `MathJsEngine.Dispose()` llama `_initTask.Wait()` dentro de un try/catch para absorber fallos de inicialización, luego adquiere el lock, llama `_engine.Dispose()` y pone `_engine = null`. Esto garantiza que no haya evaluaciones en curso cuando se libera el engine.

**La inicialización arranca en el momento de la resolución DI**: `MathJsEngine` está registrado como singleton y su constructor lanza `Task.Run(Initialize)` inmediatamente. Esto significa que el background thread de inicialización empieza cuando el contenedor construye el singleton — antes de que `GlobalSearch.Start()` lo solicite explícitamente — lo que amplía el tiempo disponible para el warmup.

## Unidades custom

Al arrancar `mathjs-helpers.js` registra unidades custom en math.js (justo después de `math.createUnit('USD')`):

- **Velocidad**: `kmh` y `mph` como unidades simples en la dimensión `m/s`.
- **Rotación**: `rpm` en la dimensión `1/s`.
- **Tasas de datos**: `bps`, `kbps`, `Mbps`, `Gbps`, `Tbps` registradas individualmente para nombres exactos.

Estas unidades son necesarias porque math.js no las incluye por defecto y porque los tests de snapshot (`mathjs-unit-snapshot.json`) verifican que el registry no cambie inesperadamente.

## Normalización de unidades

math.js es case-sensitive: `kg` y `KG` son tokens distintos (el segundo es inválido). Para que el usuario pueda escribir `KG`, `Km` o `MILES` sin preocuparse por el case, `mathjs-helpers.js` (`Yottacast.Core/Search/Calculator/mathjs-helpers.js`) mantiene mapas de normalización que se aplican sobre el AST antes de evaluar.

### Datos precomputados

**`mathjs-precomputed.json`** (`Yottacast.Core/Search/Calculator/mathjs-precomputed.json`, embedded resource) — generado por `mathjs-precompute.js` (`Yottacast.Core/Search/Calculator/mathjs-precompute.js`). Contiene tres estructuras:
- `symbols`: lista de todos los tokens canónicos del registry de math.js.
- `ambiguous`: mapa `lowercase → [{symbol, longName}]` solo para tokens con múltiples formas canónicas distintas.
- `functionNames`: mapa `lowercase → canonical` de nombres de funciones math.js (para `SQRT` → `sqrt`).

Se inyecta en el engine con `loadPrecomputedData()`. A partir de él se construyen:
- `_unitSymbols` — `lowercase → canonical` para tokens no ambiguos.
- `_unitAmbiguousMap` — `lowercase → [{symbol, longName}]` para tokens ambiguos.

El propósito es la cobertura automática total del registry de math.js: sin este mapa, `resolveUnitToken('KG')` no sabría que debe devolver `kg`. `unit-config.json` solo contiene excepciones manuales para unos pocos casos especiales; `mathjs-precomputed.json` cubre los cientos de combinaciones prefijo+unidad automáticamente.

### Configuración manual: `unit-config.json`

Embedded resource en `Yottacast.Core/Search/Calculator/unit-config.json`. Se carga con `loadAliasData()` y define:

- **`inputAliases`**: aliases de entrada con caracteres especiales que se reemplazan antes del parseo (ej. `"°c"` → `"degC"`). Aplicados por `NormalizeExpressionCore` en `MathJsEngine.cs` antes de llamar al JS.
- **`tokenAliases`**: overrides de tokens en el traversal del AST. Incluye aliases de un solo carácter con case significativo (ej. `"c"` → `"degC"`, `"v"` → `"V"`), formas plurales de unidades cuyo canónico es la forma corta (ej. `"hours"` → `"h"`, `"seconds"` → `"s"`), y aliases de velocidad (ej. `"kmph"` → `"kmh"`). Se mergen en `_unitOverrides`.
- **`evalSafeAliases`**: sustituciones que se aplican en el nombre del nodo AST antes de que la expresión llegue a `math.evaluate()`, para evitar colisiones con funciones de math.js. Por ejemplo, `"min"` → `"minute"` evita que math.js interprete `min` como la función `math.min`. El resultado se re-transforma para display vía `displayNames` (ver abajo), de modo que el usuario escribe `min` y ve `min` en el resultado.
- **`displayNames`**: nombres de display para el resultado final (ej. `"degC"` → `"°C"`, `"minute"` → `"min"`). Usados por `DisplayUnit()` en `MathJsEngine.cs`. Solo aplica a la unidad en formato corto (`fromShort`/`toShort`); los nombres largos se construyen aparte.
- **`longNames`**: nombres largos explícitos para unidades simples que no tienen forma larga derivable automáticamente (ej. `"h"` → `"hour"`, `"degC"` → `"celsius"`). Las unidades compuestas con ` / ` no necesitan entradas aquí — se derivan automáticamente de sus componentes (ver "Nombres largos"). Las entradas donde clave y valor son iguales (ej. `"day":"day"`) solo sirven para habilitar la pluralización (`"10 days"`). `loadAliasData()` genera automáticamente el mapeado inverso (ej. `longNames["h"]="hour"` → `_unitOverrides["hour"]="h"`), de modo que `"10 hour"` se normaliza igual que `"10h"`.
- **`defaultTargets`**: mapa `unidad → target` para la conversión por defecto cuando el usuario escribe solo `valor + unidad`. Prioridad máxima en `findDefaultTarget`: se consulta antes que `defaultPairs`. Necesario para unidades cuyo target natural difiere del que daría el par dimensional — por ejemplo `"g": "oz"` (no `"kg"`) o `"m / s": "km / h"` (no `"mi / h"`). Las entradas que coinciden exactamente con un par (ej. `"kg": "lb"`) son redundantes con `defaultPairs` pero pueden mantenerse por claridad. Actúa como fallback cuando el intercept de `normalizeUnits` no produce un resultado interesante. Ver los valores en `unit-config.json`.
- **`defaultPairs`**: lista de pares `[A, B]` para matching dimensional. Fallback cuando la unidad no tiene entrada directa en `defaultTargets`. `findDefaultTarget` compara la dimensión física de la unidad con `A` usando `math.Unit.equalBase`; si coincide, devuelve `A` (salvo que la unidad ya sea `A`, en cuyo caso devuelve `B`). Cubre automáticamente todas las variantes con prefijo no enumeradas: con el par `["kg", "lb"]`, cualquier unidad de masa no listada (`Mg`, `ng`…) devuelve `"kg"`. La elección de `A` importa solo para esas unidades no listadas — `"kg"` (no `"g"`) porque `"10 Mg → kg"` es más legible que `"10 Mg → 10000000 g"`.
- **`normalizeUnits`**: lista de unidades que activan el modo de descomposición natural (tiempo) o selección de mejor unidad (datos). Ver la sección "Normalización natural" más abajo.
- **`blocked`**: tokens bloqueados — no se reconocen como unidades (ej. símbolos históricos ambiguos o inutilizables).

### Resolución de tokens: `resolveUnitToken`

Definida en `mathjs-helpers.js`. Aplica los checks en este orden:

1. **Bloqueado** (`_blockedUnits`) — descartado inmediatamente.
2. **Override exacto** (`_unitOverrides[name]`) — override de case exacto. Cubre tokens de un solo carácter donde el case importa (`"c"` → `degC` pero `"C"` → Coulomb).
3. **Override lowercase para multi-char** — para tokens de más de un carácter, también se busca `_unitOverrides[name.toLowerCase()]`. Esto hace que `"Hour"`, `"HOUR"`, `"Celsius"`, `"FAHRENHEIT"` etc. funcionen igual que sus formas en minúscula. Los tokens de un solo carácter se excluyen intencionalmente de este fallback para preservar la distinción case-sensitive (`"c"` ≠ `"C"`).
4. **Sinónimos** — si todos los candidatos del mapa ambiguo comparten el mismo `longName` (ej. `l` y `L` son ambos "litre"), se normaliza al primer canónico sin marcar como ambiguo.
5. **Ya canónico** — si el input ya es exactamente uno de los canónicos con distinto significado, se devuelve tal cual sin ambigüedad.
6. **Verdaderamente ambiguo** — múltiples candidatos con distintos significados. Se devuelve el primero con `ambiguous: true` y la lista de candidatos para mostrar la pista al usuario. Ejemplo: `mg` colisiona con `Mg` (miligramo vs megagramo).

Las ambigüedades surgen casi siempre de la colisión entre pares de prefijos que solo se diferencian en case (`M`/`m`, `P`/`p`, `Z`/`z`, `Y`/`y`) aplicados a unidades del grupo SHORT.

### Normalización de expresiones: `normalizeExpression`

Función JS en `mathjs-helpers.js` que: parsea el AST, elimina bloques y asignaciones, recorre los nodos aplicando resolución de unidades + `_evalSafeAliases` + normalización de funciones, detecta ambigüedades y monedas, y determina el `kind` de la expresión:

- **`calculation`**: expresión aritmética sin conversión de unidades.
- **`unit_entry`**: `valor unidad` implícito con target por defecto conocido (ej. `10 km` → añade `to mile`). También se activa para unidades compuestas (ver más abajo).
- **`simple_conversion`**: `valor unidad to unidad`.
- **`complex_conversion`**: `expresión to unidad` — cualquier expresión que incluya `to` pero cuya parte izquierda no es un simple `número × símbolo`.

El record C# es `NormalizedExpression` (en `MathJsEngine.cs`).

### Unidades compuestas (`número × unidad / unidad`)

Expresiones como `10 km/h` producen en el AST un `OperatorNode('/')` en el que el numerador es un `OperatorNode('*', implicit=true)` con un `ConstantNode` y un `SymbolNode`. El helper `_isCompoundUnitEntry(node)` en `mathjs-helpers.js` detecta este patrón exacto.

Cuando se detecta una unidad compuesta sin `to` explícito:

1. Se construye `compoundUnit = "num / den"` (ej. `"km / h"`).
2. Se busca en `defaultTargets` (directo) o en `_defaultUnitPairs` (matching dimensional). Si hay target → `kind = unit_entry`, `fromUnit = compoundUnit`.
3. Si no hay target → `kind = calculation`.

El FROM se muestra siempre tal como lo escribió el usuario. Para el matching dimensional, `defaultPairs` incluye el par `["mi / h", "km / h"]` que cubre cualquier unidad de velocidad no listada en `defaultTargets` (ej. `10 Mm/min` → TO: `mi / h`; `10 mm/s` → TO: `mi / h`).

Para `complex_conversion` con LHS compuesto, `normalizeExpression` también extrae `fromUnit` del patrón `_isCompoundUnitEntry` sobre el nodo izquierdo.

### Nombres largos: `getUnitLongName` / `getExplicitLongName`

Ambas funciones definidas en `mathjs-helpers.js`.

`getUnitLongName(symbol)` busca el nombre largo derivado de math.js: descompone el símbolo con `math.Unit.parse`, localiza el prefijo en `PREFIXES.LONG` y la unidad base en `math.Unit.UNITS` con prefijos LONG, y combina los nombres. Retorna el símbolo si no encuentra forma larga.

`getExplicitLongName(symbol)` solo consulta `_longNames` (cargado de `unit-config.json`) y retorna vacío si no hay entrada.

En C#, `GetUnitLongName` (`MathJsEngine.cs`) aplica la siguiente cadena:

1. **Override explícito** — llama a `getExplicitLongName`. Si hay entrada en `longNames`, la usa.
2. **Derivación para compuestos** — si el símbolo contiene ` / ` (ej. `"km / h"`), delega en `GetComponentLongName` para cada parte (`"km"` → "kilometer", `"h"` → "hour") y construye `"kilometer per hour"`. Así cualquier unidad compuesta tiene nombre largo automáticamente sin necesitar entrada explícita en `longNames`.
3. **Derivación math.js** — llama a `getUnitLongName`; descarta el resultado si es igual al símbolo.

`GetComponentLongName` aplica los mismos pasos 1 y 3 sobre un componente individual.

`LongForm()` en `CalculatorSearch.cs` pluraliza el nombre largo y lo compara con la forma corta; devuelve `null` si no añade información. La función `Pluralize` maneja compuestos "X per Y" pluralizando solo la primera palabra, con casos especiales para "foot" → "feet" e "inch" → "inches".

## Snapshot de unidades y detección de cambios al actualizar math.js

Hay dos archivos generados automáticamente y verificados por tests:

- **`Yottacast.Core.Tests/Search/mathjs-unit-snapshot.json`** — baseline de regresión. Captura la versión, lista de unidades, grupos de prefijos y tokens ambiguos del registry de math.js.
- **`Yottacast.Core/Search/Calculator/mathjs-precomputed.json`** — resource embebido usado en runtime. Contiene `symbols`, `ambiguous` y `functionNames`.

Ambos los genera `extractPrecomputedData()` y `extractUnitSnapshot()` en `mathjs-precompute.js`, que carga `math.min.js` y `mathjs-helpers.js` (en ese orden — `mathjs-precompute.js` llama a `getUnitLongName()` que está definida en helpers).

El test `MathJsGeneratedFilesTests.GeneratedFiles_MatchCommittedBaseline` (clase en `Yottacast.Core.Tests/Search/MathJsUnitSnapshotTests.cs`, colección `"MathJsSnapshot"`) regenera ambos archivos en memoria y los compara con los comprometidos. Si difieren, falla con un diff legible:

```
math.js unit data changed. Delete the snapshot files and re-run tests to regenerate.
  Version: 11.12.0 → 11.13.0
  New units (2): furlong, league
  New ambiguous tokens (regression): mpa, pa
```

**Fixture dedicada**: el test usa `MathJsSnapshotFixture` con `EmptyCurrencyRateProvider` para no contaminar el snapshot con divisas registradas por otros tests.

**Workflow al actualizar math.js**:
1. Cambiar la URL de descarga en `Yottacast.Core/Yottacast.Core.csproj` a la nueva versión
2. Borrar `Yottacast.Core/Search/Calculator/math.min.js` (se redescarga en el siguiente build)
3. `dotnet build`
4. Borrar `Yottacast.Core.Tests/Search/mathjs-unit-snapshot.json` y `Yottacast.Core/Search/Calculator/mathjs-precomputed.json`
5. `dotnet test --filter GeneratedFiles_MatchCommittedBaseline` — los regenera
6. `dotnet build` para re-embedder el nuevo `mathjs-precomputed.json`
7. `dotnet test` para verificar que todo pasa

## Normalización natural (`normalizeUnits`)

Cuando el usuario escribe un único valor con unidad (`unit_entry`) y esa unidad pertenece a `normalizeUnits`, `MathJsEngine.EvaluateSimple` intercepta la evaluación antes de usar `defaultTargets` y llama a `TryNormalize`. Si el resultado es "interesante" (unidad o componentes distintos a la entrada), se devuelve directamente; si es trivial (misma unidad, mismo valor), `TryNormalize` retorna `null` y la evaluación cae al comportamiento habitual de `defaultTargets`.

### Modos de normalización

Hay dos modos, configurados por el campo `mode` de cada cadena en `_normalizeChains` (`mathjs-helpers.js`):

**`decompose` (tiempo)** — descompone el valor en hasta 3 componentes, de mayor a menor (año → día → hora → minuto → segundo → milisegundo). Ejemplos: `38000s → 10h 33min 20s`, `49h → 2 day 1 h`. El resultado multi-componente usa `ToUnit=""` y `ToUnitLong` como string largo pre-formateado; `CalculatorSearch` lo detecta y lo usa directamente en el `toLong`.

**`best_unit` (datos)** — encuentra la unidad más alta donde el valor ≥ 1 y lo expresa con hasta 3 decimales. Ejemplos: `1500 MB → 1.5 GB`, `0.01 GB → 10 MB`. El resultado es siempre un único componente.

### Implementación

- **JS**: `computeNormalization(valueStr, unit)` en `mathjs-helpers.js` — convierte el valor a la unidad base de la cadena (ej. segundos para tiempo, bytes para datos) y aplica el algoritmo según el `mode`. `formatMaxDec(value, maxDec)` formatea con máx `maxDec` decimales eliminando ceros finales.
- **C#**: `TryNormalize(normalized, hints)` en `MathJsEngine.cs` — llama a `EvalJs($"{lhsExpr} to {origUnit}")` (con target explícito para evitar la auto-normalización SI de math.js en el from), invoca `computeNormalization` vía JS y construye el `ConversionResult`. `FormatNormalizedShort` / `FormatNormalizedLong` componen el string multi-componente; `PluralizeName` replica la lógica de `CalculatorSearch.Pluralize`.

### Preservación del from-side

El intercept usa `EvalJs("... to origUnit")` en lugar de `EvalJs("...")`. Esto fija la unidad de salida e impide que math.js elija un prefijo SI automáticamente. Como resultado, el `fromShort` refleja siempre la entrada literal del usuario (`0.001 s` → from: `"0.001 s"`, to: `"1 ms"`), a diferencia de unidades SI estándar fuera de `normalizeUnits` donde el from se auto-normaliza hacia abajo (`0.001 V` → from: `"1 mV"`).

## CalculatorSearch

**Detección de expresiones**: la clasificación la hace `normalizeExpression()` vía análisis del AST. El `kind` determina el camino de evaluación: `calculation` para aritmética, `unit_entry` para un valor con unidad que tiene conversión por defecto, `simple_conversion` y `complex_conversion` para expresiones con `to`/`in`.

**Conversiones de unidades**:
- Formato explícito: `NÚMERO UNIDAD (to|in) UNIDAD` — ej. `10 kg to lbs`, `100 F to C`, `10 mi/s to km/h`
- Formato implícito: `NÚMERO UNIDAD` — ej. `10 km`, `60 km/h` se convierte automáticamente usando `defaultTargets`
- math.js las evalúa nativamente; `normalizeExpression` normaliza el case antes de evaluar

**Divisas**: soportadas vía `ICurrencyRateProvider`. Las tasas se registran dinámicamente en el engine con `registerCurrency()` en cada llamada a `Evaluate()`, actualizándose si la tasa ha cambiado. Los códigos de divisa (ej. `USD`, `EUR`) se normalizan a mayúsculas en el AST. Al escribir una sola divisa (ej. `10 USD`), se convierte al par por defecto definido en `_defaultCurrencyPair` en `mathjs-helpers.js` (`['EUR', 'USD']`).

**`ConversionResultItemViewModel`** (`Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`): resultado de conversión con tres pares de campos (from original, from normalizado, to), navegación de celdas y `INotifyPropertyChanged`:

- `FromShort` / `FromLong`: from tal como lo escribió el usuario, bien formateado (ej. `"0.001 V"` / `"0.001 volts"`). `FromLong` es `null` si no añade información.
- `NormFromShort` / `NormFromLong`: from auto-simplificado por math.js (ej. `"1 mV"` / `"1 millivolt"`); `null` si no hubo simplificación.
- `ToShort` / `ToLong`: destino de la conversión (ej. `"6.213711922 mile"` / `"6.213711922 miles"`). `ToLong` es `null` si no añade información.
- `FromWasNormalized`: `true` cuando `NormFrom*` está presente — activa la navegación ←/→ y los highlights de celda.
- `SelectedCell` (`ConversionCell` enum: `To`, `NormFrom`, `OrigFrom`): celda con el foco actual; por defecto `To`. Al cambiar, dispara `PropertyChanged` para las tres propiedades `Is*Highlighted`.
- `MoveCellLeft()` / `MoveCellRight()`: desplazan la selección y devuelven `true` si el movimiento fue consumido, `false` si ya estaban en la celda extrema (lo que permite que el TextBox mueva el cursor de texto).

**Display en la UI**: cuando `FromWasNormalized = false`, el resultado muestra dos celdas (`[From] → [To]`). Cuando `FromWasNormalized = true`, muestra tres celdas (`[From original] → [From normalizado] → [To]`); la celda NormFrom y la segunda flecha tienen `IsVisible` enlazado a `FromWasNormalized` y se colapsan automáticamente cuando no hay simplificación.

**Normalización del FROM**: hay varios mecanismos que pueden cambiar la unidad mostrada en FROM:

1. **Auto-simplificación SI de math.js** — para unidades SI simples con coeficiente < 1, math.js reescribe al prefijo más conveniente (`0.001 V` → `1 mV`). Solo ocurre hacia abajo; `1000 m` permanece como `1000 m`. Las imperiales y no-SI nunca se simplifican. Cuando ocurre, `FromValue`/`FromUnit` preservan el original del usuario y `NormFromValue`/`NormFromUnit` almacenan la forma simplificada; `FromWasNormalized = true`.
2. **Forzado `to {fromUnit}` para compuestos** — en `EvaluateSimple` e `EvaluateComplex`, si `fromUnit` contiene ` / `, se evalúa el LHS forzando la unidad (`EvalJs("10 km/h to km / h")`). Esto evita que math.js auto-simplifique a una unidad custom registrada de la misma dimensión (ej. `kmh` o `mph`). El usuario ve la unidad exactamente como la escribió.
3. **`TryNormalize` (tiempo/datos)** — previene la auto-simplificación SI forzando la unidad original con `to origUnit`. `FromWasNormalized` siempre es `false` para estas unidades.

**`ISearchHintProvider` / `LastHint`**: `CalculatorSearch` implementa `ISearchHintProvider`. Para errores `UnknownSymbol` e `IncompatibleUnits`, `LastHint` se establece con un mensaje legible para el usuario. Para otros errores (sintaxis, etc.) no se muestra hint.

**No-result cuando el resultado coincide con la query**: si `Evaluate()` devuelve exactamente la misma cadena que la query de entrada (por ejemplo, al escribir sólo un número como `42`), `Search` no devuelve ningún resultado.

**Display contract**: el `Title` del resultado es el valor de destino en formato corto; el `Subtitle` es la query normalizada (`NormalizedQuery`), opcionalmente seguida del hint de ambigüedad. El icono es "🧮" para calculadora y "📐" para conversor.

`CalculatorSearch` tiene un score de 4, mayor que otras fuentes, por lo que sus resultados aparecen cerca de la cima cuando la query es reconocida.

**`Start()` es no-op**: a diferencia de otras instant sources, `CalculatorSearch.Start()` no inicia ningún proceso. `WhenReady()` delega directamente en `engine.WhenReady()`.

**Activación**: al activar un resultado se copia al portapapeles — el resultado aritmético (`RawValue`) para calculadora. Para conversiones, se copia la celda seleccionada: `OrigFrom` copia `fromShort`, `NormFrom` copia `normFromShort` (o `toShort` si el normalizado no está disponible), `To` copia `toShort`. La celda por defecto es `To`, de modo que sin navegar, Enter copia el destino como antes.

**El parámetro `limit` se ignora**: `CalculatorSearch.Search()` acepta `limit` por contrato de `IInstantSearchSource` pero nunca lo usa. La fuente devuelve como máximo un elemento.

**Tests**: repartidos en varias clases xUnit que usan `MathJsEngineFixture` (colección `"MathJs"`) para inicializar el engine una sola vez:
- `Yottacast.Core.Tests/Search/Calculator/CalculatorSearchTests.cs` — aritmética y funciones
- `Yottacast.Core.Tests/Search/Calculator/UnitConverterSearchTests.cs` — conversiones de unidades
- `Yottacast.Core.Tests/Search/Calculator/DefaultConversionTests.cs` — conversiones por defecto y nombres largos
- `Yottacast.Core.Tests/Search/Calculator/ClassifyErrorTests.cs` — clasificación de errores
- `Yottacast.Core.Tests/Search/Calculator/NormalizeExpressionTests.cs` — normalización de expresiones y detección de kinds

## ClipboardService

Core no depende de Avalonia. `App.axaml.cs` llama `clipboardService.Initialize(...)` una vez al arranque, pasando un delegate que envuelve la operación en `Dispatcher.UIThread.InvokeAsync()` para garantizar que el acceso al portapapeles ocurra en el hilo UI, y luego llama `TopLevel.GetTopLevel(mainWindow)?.Clipboard?.SetTextAsync(text)`.

**No-op antes de inicializar**: `CopyText()` invoca `_copy?.Invoke(text)` — si se llama antes de que `App.axaml.cs` haya ejecutado `Initialize()`, el texto se descarta silenciosamente. En la práctica esto nunca ocurre porque la UI no es interactiva hasta que las instant sources están `Ready`.

**Testabilidad**: en tests, `ClipboardService` se instancia directamente y se inicializa con un delegate de captura (`clipboard.Initialize(text => copied = text)`), sin necesidad de Avalonia. Esto permite verificar que `OnActivate` copia el valor correcto sin levantar la UI.

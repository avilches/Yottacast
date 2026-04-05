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
- **`tokenAliases`**: overrides de tokens en el traversal del AST. Incluye aliases de un solo carácter con case significativo (ej. `"c"` → `"degC"`, `"v"` → `"V"`) y formas plurales de unidades cuyo canónico es la forma corta (ej. `"hours"` → `"h"`, `"seconds"` → `"s"`). Se mergen en `_unitOverrides`.
- **`evalSafeAliases`**: sustituciones que se aplican en el nombre del nodo AST antes de que la expresión llegue a `math.evaluate()`, para evitar colisiones con funciones de math.js. Por ejemplo, `"min"` → `"minute"` evita que math.js interprete `min` como la función `math.min`. El resultado se re-transforma para display vía `displayNames` (ver abajo), de modo que el usuario escribe `min` y ve `min` en el resultado.
- **`displayNames`**: nombres de display para el resultado final (ej. `"degC"` → `"°C"`, `"minute"` → `"min"`). Usados por `DisplayUnit()` en `MathJsEngine.cs`. Solo aplica a la unidad en formato corto (`fromShort`/`toShort`); los nombres largos se construyen aparte.
- **`longNames`**: nombres largos explícitos para unidades que no tienen forma larga derivable automáticamente vía el grupo de prefijos LONG (ej. `"h"` → `"hour"`, `"degC"` → `"celsius"`). Sirven para dos propósitos: (1) display — `getExplicitLongName()` los usa para mostrar `"10 hours"` en lugar de `"10 h"`; (2) reconocimiento de input — `loadAliasData()` genera automáticamente el mapeado inverso (ej. `longNames["h"]="hour"` → `_unitOverrides["hour"]="h"`), de modo que `"10 hour"` se normaliza igual que `"10h"`. Las entradas donde clave y valor son iguales (ej. `"day":"day"`) solo sirven para habilitar la pluralización (`"10 days"`).
- **`defaultTargets`**: mapa `unidad → target` para la conversión por defecto cuando el usuario escribe solo `valor + unidad`. Las conversiones priorizan pares métrico↔imperial. Ver los valores en `unit-config.json`.
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
- **`unit_entry`**: `valor unidad` implícito con target por defecto conocido (ej. `10 km` → añade `to mile`).
- **`simple_conversion`**: `valor unidad to unidad`.
- **`complex_conversion`**: `expresión to unidad`.

El record C# es `NormalizedExpression` (en `MathJsEngine.cs`).

### Nombres largos: `getUnitLongName` / `getExplicitLongName`

Ambas funciones definidas en `mathjs-helpers.js`.

`getUnitLongName(symbol)` busca el nombre largo derivado de math.js: descompone el símbolo con `math.Unit.parse`, localiza el prefijo en `PREFIXES.LONG` y la unidad base en `math.Unit.UNITS` con prefijos LONG, y combina los nombres. Retorna el símbolo si no encuentra forma larga (ej. unidades de tiempo que usan prefijos NONE en math.js).

`getExplicitLongName(symbol)` solo consulta `_longNames` (cargado de `unit-config.json`) y retorna vacío si no hay entrada. En C#, `GetUnitLongName` (en `MathJsEngine.cs`) llama primero a `getExplicitLongName` — si hay override explícito lo usa directamente; si no, llama a `getUnitLongName` y descarta el resultado si es igual al símbolo.

`LongForm()` en `CalculatorSearch.cs` pluraliza el nombre largo y lo compara con la forma corta; devuelve `null` si no añade información (ej. si `"10 kilometer"` ya es distinto de `"10 km"`, devuelve `"10 kilometers"`).

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

## CalculatorSearch

**Detección de expresiones**: la clasificación la hace `normalizeExpression()` vía análisis del AST. El `kind` determina el camino de evaluación: `calculation` para aritmética, `unit_entry` para un valor con unidad que tiene conversión por defecto, `simple_conversion` y `complex_conversion` para expresiones con `to`/`in`.

**Conversiones de unidades**:
- Formato explícito: `NÚMERO UNIDAD (to|in) UNIDAD` — ej. `10 kg to lbs`, `100 F to C`
- Formato implícito: `NÚMERO UNIDAD` — ej. `10 km` se convierte automáticamente usando `defaultTargets` (ver `unit-config.json`)
- math.js las evalúa nativamente; `normalizeExpression` normaliza el case antes de evaluar

**Divisas**: soportadas vía `ICurrencyRateProvider`. Las tasas se registran dinámicamente en el engine con `registerCurrency()` en cada llamada a `Evaluate()`, actualizándose si la tasa ha cambiado. Los códigos de divisa (ej. `USD`, `EUR`) se normalizan a mayúsculas en el AST. Al escribir una sola divisa (ej. `10 USD`), se convierte al par por defecto definido en `_defaultCurrencyPair` en `mathjs-helpers.js` (`['EUR', 'USD']`).

**`ConversionResultItemViewModel`** (`Yottacast.Core/ViewModels/ConversionResultItemViewModel.cs`): resultado de conversión con cuatro campos:
- `FromShort` / `ToShort`: valor + unidad en formato corto (ej. `"10 km"`, `"6.213711922 mile"`)
- `FromLong` / `ToLong`: forma larga con pluralización (ej. `"10 kilometers"`, `"6.213711922 miles"`); `null` si no añade información sobre la forma corta

**Normalización automática de prefijo SI en `FromShort`**: cuando el valor introducido tiene un coeficiente < 1 en la unidad original (ej. `0.001 V`), `math.format()` reescribe automáticamente el from al prefijo SI más conveniente (`0.001 V` → `1 mV`, `0.01 s` → `10 ms`). Esta normalización solo ocurre hacia abajo (coeff < 1): `1000 m` permanece como `1000 m`, no se convierte a `1 km`. Las unidades imperiales y no-SI (ft, oz, atm, psi, hp, acre…) nunca se normalizan — conservan el valor tal como lo escribió el usuario. Este comportamiento está verificado en `DefaultConversionTests.FromUnit_AutoNormalizesToBestSIPrefix`.

**Ambigüedades**: cuando un token es ambiguo (ej. `mg` puede ser miligramo o megagramo), el `Subtitle` del resultado incluye una advertencia `⚠ 'mg', mg=milligram · Mg=megagram` con los candidatos. El primero de la lista se usa para la evaluación.

**`ISearchHintProvider` / `LastHint`**: `CalculatorSearch` implementa `ISearchHintProvider`. Para errores `UnknownSymbol` e `IncompatibleUnits`, `LastHint` se establece con un mensaje legible para el usuario. Para otros errores (sintaxis, etc.) no se muestra hint.

**No-result cuando el resultado coincide con la query**: si `Evaluate()` devuelve exactamente la misma cadena que la query de entrada (por ejemplo, al escribir sólo un número como `42`), `Search` no devuelve ningún resultado.

**Display contract**: el `Title` del resultado es el valor de destino en formato corto; el `Subtitle` es la query normalizada (`NormalizedQuery`), opcionalmente seguida del hint de ambigüedad. El icono es "🧮" para calculadora y "📐" para conversor.

`CalculatorSearch` tiene un score de 4, mayor que otras fuentes, por lo que sus resultados aparecen cerca de la cima cuando la query es reconocida.

**`Start()` es no-op**: a diferencia de otras instant sources, `CalculatorSearch.Start()` no inicia ningún proceso. `WhenReady()` delega directamente en `engine.WhenReady()`.

**Activación**: al activar un resultado se copia al portapapeles — el resultado aritmético (`RawValue`) para calculadora, o el valor de destino en formato corto (`toShort`) para conversiones.

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

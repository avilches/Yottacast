# Calculadora y conversor de unidades

Implementado como `IInstantSearchSource`: `CalculatorSearch`. Maneja tanto expresiones matemáticas como conversiones de unidades.

## Motor: MathJsEngine

`MathJsEngine` (`Search/Calculator/MathJsEngine.cs`) — singleton que carga math.js embebido en la DLL (embedded resource en `Yottacast.Core/Search/Calculator/math.min.js`) dentro de un engine Jint 3.x. La inicialización se hace en un background thread; hasta que `WhenReady()` se complete, `Evaluate()` devuelve `null`.

**Configuración del engine**: se crea con un límite de recursión (ver `MathJsEngine`).

**WarmUp**: al final de `Initialize()` se ejecuta `math.evaluate('1+1')` para disparar la compilación JIT de Jint, de modo que la primera query real del usuario sea instantánea.

**Thread safety**: un `lock (_lock)` protege el acceso al engine durante cada llamada a `Evaluate()`. Es seguro llamarlo desde múltiples hilos.

**Escape de entrada**: antes de pasarla a math.js, la expresión tiene las barras invertidas escapadas (`\` → `\\`) y las comillas simples escapadas (`'` → `\'`).

**Formateo de resultados**: los resultados se formatean con `math.format(r, { precision: 10 })` — 10 dígitos significativos — para evitar ruido de coma flotante como `22.046226218487758`.

**Double-checked null guard**: `Evaluate()` comprueba `_engine == null` antes de adquirir el lock y de nuevo dentro de él. La comprobación exterior garantiza un fast-path sin contención cuando el engine todavía no está listo; la interior garantiza corrección ante una hipotética carrera con `Dispose()`.

**`Evaluate()` devuelve `null` si el resultado es whitespace**: además de capturar excepciones, descarta resultados vacíos (`string.IsNullOrWhiteSpace`) antes de devolverlos.

**Manejo de errores**: `Evaluate()` captura silenciosamente todas las excepciones (errores de sintaxis, división por cero, etc.) y devuelve `null`. Las expresiones inválidas no producen ningún resultado.

**math.js descargado en build**: el `.csproj` de Core incluye un target `DownloadMathJs` que ejecuta `curl` si el fichero no existe. El fichero se excluye del repositorio (`.gitignore`). El primer `dotnet build` lo descarga automáticamente.

**Gotcha — versión de math.js incompatible con Jint**: versiones recientes de math.js lanzan "Assignment to constant variable" dentro de Jint 3.x al ejecutar `math.evaluate`. La versión de math.js embebida está fijada; ver el target `DownloadMathJs` del `.csproj`. Si se actualiza Jint a una versión con soporte ES2022+, se puede probar una versión más reciente de math.js.

**Gotcha — EmbeddedResource condicional**: el `<EmbeddedResource>` de `math.min.js` tiene `Condition="Exists(...)"`. Si `curl` falla en el build, la compilación termina correctamente pero el recurso no queda embebido. En ese caso la app lanza `InvalidOperationException` en runtime al intentar cargar el stream del recurso.

**Dispose**: `MathJsEngine.Dispose()` llama `_initTask.Wait()` dentro de un try/catch para absorber fallos de inicialización, luego adquiere el lock, llama `_engine.Dispose()` y pone `_engine = null`. Esto garantiza que no haya evaluaciones en curso cuando se libera el engine.

**La inicialización arranca en el momento de la resolución DI**: `MathJsEngine` está registrado como singleton y su constructor lanza `Task.Run(Initialize)` inmediatamente. Esto significa que el background thread de inicialización empieza cuando el contenedor construye el singleton — antes de que `GlobalSearch.Start()` lo solicite explícitamente — lo que amplía el tiempo disponible para el warmup.

## Normalización de mayúsculas/minúsculas en unidades

math.js es case-sensitive: `kg` y `KG` son tokens distintos (el segundo es inválido). Para que el usuario pueda escribir `KG`, `Km` o `MILES` sin preocuparse por el case, `mathjs-helpers.js` construye un mapa de normalización en startup y lo aplica antes de evaluar.

**`_unitTokenMap`**: se construye una sola vez al cargar el script, iterando `math.Unit.UNITS` y aplicando todos sus prefijos de `math.Unit.PREFIXES`. Para cada combinación `(prefijo + unidad)` se registra la forma canónica bajo su versión en minúsculas. El resultado es un mapa `lowercase → [canonicals]`.

**Tokens unívocos vs ambiguos**:
- Si un lowercase tiene un solo canónico → el token puede escribirse en cualquier capitalización; `_resolveUnitToken` lo normaliza automáticamente. Ejemplos: `kg`→`kg`, `KM`→`km`, `FAHRENHEIT`→`fahrenheit`.
- Si un lowercase tiene varios canónicos → hay colisión de case real (ambigüedad). `_resolveUnitToken` solo acepta el token si ya es uno de los canónicos; de lo contrario devuelve `null` y el token se deja intacto (math.js lo rechazará si es incorrecto). Ejemplo: `mg` es ambiguo porque colisiona con `Mg` (mili-gramo vs mega-gramo); el usuario debe escribir el case exacto.

Las ambigüedades surgen casi siempre de la colisión entre pares de prefijos que solo se diferencian en case (`M`/`m`, `P`/`p`, `Z`/`z`, `Y`/`y`) aplicados a unidades del grupo SHORT. Los casos más frecuentes en uso real son `mg`/`Mg`, `mm`/`Mm`, `ms`/`mS`/`Ms`, `ml` (4 formas canónicas), `mV`/`MV`, `mW`/`MW`, `s`/`S`, `t`/`T`, `h`/`H`, `b`/`B`.

**`_unitOverrides`**: tabla de excepciones manuales que se aplica antes del mapa. Permite forzar un mapeo específico para un token concreto, independientemente del análisis automático. Definida en `mathjs-helpers.js`.

**Normalización de funciones**: el mismo paso de traversal del AST normaliza los nombres de función usando `_mathFunctionNames` (mapa `lowercase → canonical` construido desde las propiedades de `math`). Así `SQRT(2)` se convierte en `sqrt(2)`.

**`extractUnitSnapshot()` / `ExtractUnitSnapshot()`**: función JS que serializa el estado actual del registry — version de math.js, lista de unidades, grupos de prefijos, `_unitTokenMap` y lista de tokens ambiguos — como objeto JSON. `MathJsEngine.ExtractUnitSnapshot()` la invoca y devuelve el JSON formateado. Se usa exclusivamente por los tests de snapshot.

## Snapshot de unidades y detección de cambios al actualizar math.js

`Yottacast.Core.Tests/Search/mathjs-unit-snapshot.json` es un baseline comprometido en el repo que captura el estado del registry de math.js en un momento dado: versión, lista de unidades, grupos de prefijos, `tokenMap` y tokens ambiguos.

El test `MathJsUnitSnapshotTests.UnitSnapshot_MatchesCommittedBaseline` (colección `"MathJsSnapshot"`) compara el snapshot del engine en runtime contra el fichero. Si coinciden, pasa. Si difieren, falla con un diff legible:

```
math.js unit data changed. Review and update snapshot:
  MATHJS_UPDATE_SNAPSHOT=1 dotnet test --filter UnitSnapshot
  Version: 11.12.0 → 11.13.0
  New units (2): furlong, league
  New ambiguous tokens (regression): mpa, pa
```

El diff muestra solo lo relevante: unidades añadidas/eliminadas y cambios en ambigüedades (regresiones o mejoras). El `tokenMap` completo queda en el fichero JSON para inspeccionarlo con `git diff`.

**Fixture dedicada**: el test usa `MathJsSnapshotFixture` (colección propia `"MathJsSnapshot"`), que construye un engine con `EmptyCurrencyRateProvider`. Esto es necesario porque la fixture compartida `"MathJs"` tiene tests que llaman `registerCurrency`, lo que añadiría divisas como unidades al registry y contaminaría el snapshot.

**Workflow al actualizar math.js**:
1. Cambiar la URL de descarga en `Yottacast.Core.csproj` a la nueva versión
2. Borrar `Search/Calculator/math.min.js` (se redescarga en el siguiente build)
3. `dotnet build`
4. `dotnet test --filter UnitSnapshot` — falla con el diff
5. Revisar el diff; si los cambios son aceptables: `MATHJS_UPDATE_SNAPSHOT=1 dotnet test --filter UnitSnapshot`
6. El fichero `mathjs-unit-snapshot.json` queda actualizado en el source tree listo para commitear

**Consultar qué tokens son ambiguos**: la clave `ambiguous` del JSON contiene la lista completa. La clave `tokenMap` permite ver las formas canónicas de cada token.

## CalculatorSearch

**Detección de expresiones**:
- Digit + operador/paréntesis en cualquier lado: `2+2`, `(3+4)*2`, `2^10`
- Referencia a función math o constante: `sqrt(144)`, `sin(pi/2)`, `pi * r`
- Queries con un valor numérico + unidad origen + palabra clave (`to`/`in`/`en`) + unidad destino se detectan como conversión de unidades; el resto como calculadora. Ver el regex en `CalculatorSearch`.

**Conversión de unidades**:
- Formato: `NUMBER UNIT (to|in|en) UNIT`
- El número acepta tanto punto como coma como separador decimal (`10.5` o `10,5`)
- Los caracteres de unidad incluyen letras ASCII, `μ` (mu griego), `°` (grado), `/`, `²`, `³`
- Las palabras clave `to`, `in` y `en` se reconocen (case-insensitive)
- math.js las evalúa nativamente: `10 kg to lbs`, `100 fahrenheit to celsius`, `5 miles to km`

**No-result cuando el resultado coincide con la query**: si `Evaluate()` devuelve exactamente la misma cadena que la query de entrada (por ejemplo, al escribir sólo un número como `42`), `Search` no devuelve ningún resultado.

**Display contract**: el `Title` del `ResultItemViewModel` es el resultado formateado; el `Subtitle` es la query original (tal como la escribió el usuario). El icono es "🧮" para calculadora y "📐" para conversor.

`CalculatorSearch` tiene un score de 4, mayor que otras fuentes, por lo que sus resultados aparecen cerca de la cima cuando la query es reconocida.

**`Start()` es no-op**: a diferencia de otras instant sources, `CalculatorSearch.Start()` no inicia ningún proceso. `WhenReady()` delega directamente en `engine.WhenReady()` — el gating lo determina la inicialización del engine.

**Activación**: al activar un resultado de calculadora/conversor se copia el resultado al portapapeles (`OnActivate = () => clipboard.CopyText(result)`).

**Limitación de monedas**: las conversiones de divisa (`100 usd to eur`) no están soportadas — math.js no incluye tasas de cambio FX. La query es descartada porque `Evaluate()` devuelve la query original o un error.

**El parámetro `limit` se ignora**: `CalculatorSearch.Search()` acepta `limit` por contrato de `IInstantSearchSource` pero nunca lo usa. La fuente devuelve como máximo un elemento, por lo que el límite nunca aplica en la práctica.

**Tests**: los tests están repartidos en dos clases xUnit — `CalculatorSearchTests` (aritmética y funciones) y `UnitConverterSearchTests` (conversiones de unidades). Ambas usan la fixture compartida `MathJsEngineFixture` (colección `"MathJs"`) para inicializar el engine una sola vez: cargar y ejecutar ~700 KB de JS por cada clase de test sería demasiado lento.

## ClipboardService

Core no depende de Avalonia. `App.axaml.cs` llama `clipboardService.Initialize(...)` una vez al arranque, pasando un delegate que envuelve la operación en `Dispatcher.UIThread.InvokeAsync()` para garantizar que el acceso al portapapeles ocurra en el hilo UI, y luego llama `TopLevel.GetTopLevel(mainWindow)?.Clipboard?.SetTextAsync(text)`.

**No-op antes de inicializar**: `CopyText()` invoca `_copy?.Invoke(text)` — si se llama antes de que `App.axaml.cs` haya ejecutado `Initialize()`, el texto se descarta silenciosamente. En la práctica esto nunca ocurre porque la UI no es interactiva hasta que las instant sources están `Ready`.

**Testabilidad**: en tests, `ClipboardService` se instancia directamente y se inicializa con un delegate de captura (`clipboard.Initialize(text => copied = text)`), sin necesidad de Avalonia. Esto permite verificar que `OnActivate` copia el valor correcto sin levantar la UI.

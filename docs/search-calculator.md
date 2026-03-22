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

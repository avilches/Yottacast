# Calculadora y conversor de unidades

Implementado como un único `ISearchSource` instant: `CalculatorSearch`. Maneja tanto expresiones matemáticas como conversiones de unidades.

## Motor: MathJsEngine

`MathJsEngine` (`Search/Calculator/MathJsEngine.cs`) — singleton que carga math.js embebido en la DLL (embedded resource en `Yottacast.Core/Scripts/math.min.js`) dentro de un engine Jint 3.x. La inicialización se hace en un background thread; hasta que `WhenReady()` se complete, `Evaluate()` devuelve `null`.

**Configuración del engine**: se crea con un límite de recursión (ver `MathJsEngine`).

**WarmUp**: al final de `Initialize()` se ejecuta `math.evaluate('1+1')` para disparar la compilación JIT de Jint, de modo que la primera query real del usuario sea instantánea.

**Thread safety**: un `lock (_lock)` protege el acceso al engine durante cada llamada a `Evaluate()`. Es seguro llamarlo desde múltiples hilos.

**Escape de entrada**: antes de pasarla a math.js, la expresión tiene las barras invertidas escapadas (`\` → `\\`) y las comillas simples escapadas (`'` → `\'`).

**Formateo de resultados**: los resultados se formatean con precisión limitada para evitar ruido de coma flotante; ver `MathJsEngine.Evaluate`.

**Manejo de errores**: `Evaluate()` captura silenciosamente todas las excepciones (errores de sintaxis, división por cero, etc.) y devuelve `null`. Las expresiones inválidas no producen ningún resultado.

**math.js descargado en build**: el `.csproj` de Core incluye un target `DownloadMathJs` que ejecuta `curl` si el fichero no existe. El fichero se excluye del repositorio (`.gitignore`). El primer `dotnet build` lo descarga automáticamente.

**Gotcha — versión de math.js incompatible con Jint**: versiones recientes de math.js lanzan "Assignment to constant variable" dentro de Jint 3.x al ejecutar `math.evaluate`. La versión de math.js embebida está fijada; ver el target `DownloadMathJs` del `.csproj`. Si se actualiza Jint a una versión con soporte ES2022+, se puede probar una versión más reciente de math.js.

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

**No-result cuando el resultado coincide con la query**: si `Evaluate()` devuelve exactamente la misma cadena que la query de entrada (por ejemplo, al escribir sólo un número como `42`), `SearchAsync` no emite ningún resultado.

`CalculatorSearch` tiene un score mayor que otras fuentes (ver `CalculatorSearch.Score`) por lo que sus resultados aparecen cerca de la cima cuando la query es reconocida.

**Activación**: al activar un resultado de calculadora/conversor se copia el resultado al portapapeles (`OnActivate = () => clipboard.CopyText(result)`).

## ClipboardService

Core no depende de Avalonia. `App.axaml.cs` llama `clipboardService.Initialize(...)` una vez al arranque, pasando un delegate que envuelve la operación en `Dispatcher.UIThread.InvokeAsync()` para garantizar que el acceso al portapapeles ocurra en el hilo UI, y luego llama `TopLevel.GetTopLevel(mainWindow)?.Clipboard?.SetTextAsync(text)`.

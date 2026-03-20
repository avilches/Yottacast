# Calculadora y conversor de unidades

Implementado como un único `ISearchSource` instant: `CalculatorSearch`. Maneja tanto expresiones matemáticas como conversiones de unidades.

## Motor: MathJsEngine

`MathJsEngine` — singleton que carga math.js 11.12.0 embebido en la DLL (embedded resource en `Yottacast.Core/Scripts/math.min.js`) dentro de un engine Jint 3.x. La inicialización se hace en un background thread; hasta que `WhenReady()` se complete, `Evaluate()` devuelve `null`.

**math.js descargado en build**: el `.csproj` de Core incluye un target `DownloadMathJs` que ejecuta `curl` si el fichero no existe. El fichero se excluye del repositorio (`.gitignore`). El primer `dotnet build` lo descarga automáticamente.

**Gotcha — versión 13.x incompatible**: math.js 13.x lanza "Assignment to constant variable" dentro de Jint 3.x al ejecutar `math.evaluate`. Usar math.js 11.x (11.12.0 probado OK). Si se actualiza Jint a una versión con soporte ES2022+, se puede probar 12.x o 13.x.

## CalculatorSearch

**Detección de expresiones**:
- Digit + operador/paréntesis en cualquier lado: `2+2`, `(3+4)*2`, `2^10`
- Referencia a función math o constante: `sqrt(144)`, `sin(pi/2)`, `pi * r`
- Queries `N unit to unit` se detectan por regex y se evalúan como conversión; el resto como calculadora

**Conversión de unidades**:
- Formato: `NUMBER UNIT (to|in|en) UNIT`
- math.js las evalúa nativamente: `10 kg to lbs`, `100 fahrenheit to celsius`, `5 miles to km`

**Score = 4** → aparece antes que Google (Score=3) cuando la query es reconocida.

**Activación**: al activar un resultado de calculadora/conversor se copia el resultado al portapapeles (`OnActivate = () => clipboard.CopyText(result)`).

## ClipboardService

Core no depende de Avalonia. `App.axaml.cs` llama `clipboardService.Initialize(...)` una vez al arranque, pasando un delegate que usa `TopLevel.GetTopLevel(mainWindow)?.Clipboard`.

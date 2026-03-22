# Logging

Configurado con Serilog en `BuildServices()`. Los logs se escriben en fichero rotatorio diario (retención de 7 días):

- macOS: `~/Library/Logs/Yottacast/yottacast-<fecha>.log`
- Windows/Linux: `%LOCALAPPDATA%\Yottacast\Logs\yottacast-<fecha>.log`

El nivel mínimo es `Debug`. Todos los servicios reciben `ILogger<T>` por inyección.

## Output template

El fichero de log (GUI) usa:

```
{Timestamp:HH:mm:ss.fff} [{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
```

La consola del CLI omite el timestamp:

```
[{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
```

`SourceContext` se rellena automáticamente con el nombre de la clase genérica (`ILogger<T>`), lo que identifica inequívocamente el origen de cada línea sin ningún esfuerzo manual.

## Integración con DI

En la GUI, Serilog se envuelve en `Microsoft.Extensions.Logging` mediante `AddSerilog(serilogLogger, dispose: true)` dentro de `BuildServices()`. El flag `dispose: true` garantiza que el fichero de log se cierra y vacía correctamente al salir.

En el CLI (`Yottacast.Cli/Program.cs`) no hay contenedor DI: se instancia un `SerilogLoggerFactory` directamente y los loggers se crean con `LoggerFactory.CreateLogger<T>()` y se pasan manualmente a cada servicio. El destino es la consola (sin fichero).

## Qué se loguea por área

- **Migraciones de versión** (`App`): `Information` al detectar cambio de versión entre `LastLaunchedVersion` y la versión actual.
- **Búsqueda de apps** (`ApplicationSearch`): `Information` al inicio y fin del escaneo de directorios; `Debug` por cada búsqueda con recuento de resultados en caché.
- **Búsqueda de documentos** (`UserDocumentSearch`): `Debug` al iniciar cada query con timeout y carpetas configuradas; `Information` al completarla (total de resultados o estado de cancelación).
- **Spotlight** (`MacOsPlatformProvider`): `Warning` si alguna carpeta configurada no existe; `Debug` con el predicado y scope enviados a Spotlight, y con el resultado (elapsed, cancelled, count, error).
- **Emojis** (`EmojiDataLoader`): `Information` en cada fase de carga (caché en disco, caché embebida, recurso embebido crudo) con tiempo transcurrido y número de entradas; `Warning` ante cualquier fallo de lectura o parseo; `Information` al escribir la caché en disco.
- **Temas** (`ThemeService`): `Warning` si no se puede leer la carpeta de temas o si un fichero de tema no existe; `Information` al aplicar un tema con éxito; `Warning` ante error al aplicar.
- **Actualizaciones** (`UpdateChecker`): `Information` si hay una versión más nueva disponible; `Warning` ante cualquier fallo de red o parseo.
- **Configuración de usuario** (`UserSettings`): `Information` al cargar el fichero con éxito (ruta); `Information` si el fichero no existe o es inválido, indicando que se crean valores por defecto; `Debug` cada vez que se guarda (ruta); `Warning` si el guardado falla.
- **Auto-reparación de browser/terminal** (`UserSettings`): cuando `ActiveBrowser` o `ActiveTerminal` detectan que el valor configurado ya no existe en disco y eligen un sustituto, emiten `Information` con el nombre anterior y el nuevo. Este log aparece la primera vez que se accede a esas propiedades (típicamente al abrir Settings o al ejecutar una búsqueda web).
- **Tema por defecto embebido** (`ThemeService`): cuando `ApplyBuiltinDefault()` aplica los valores hardcoded (bien porque el fichero de tema no existe, bien por error en la carga), emite `Information` con el mensaje `"Theme applying built-in default"`. Es un caso distinto al log de éxito de `Apply()`, que registra el nombre del tema del fichero JSON.

## Clases sin logging

Las siguientes clases reciben `ILogger<T>` por inyección pero no emiten ningún log en su implementación actual:

- **`ProcessRunner`**: el logger se inyecta pero nunca se usa; los errores de proceso (cancelación, excepción) se capturan y devuelven en `ProcessResult` sin loguear nada.
- **`BrowserDiscovery`** y **`TerminalDiscovery`**: el logger se inyecta pero no se invoca en ningún método.

Las siguientes clases no tienen `ILogger` y sus fallos son silenciosos:

- **`MathJsEngine`**: los errores de evaluación de math.js se capturan con un `catch` vacío que devuelve `null`; el fallo de inicialización también se silencia en `Dispose`.
- **`EmojiSearch`**: si la tarea de carga falla (`t.IsFaulted`), se ignora sin loguear nada (el logging de la carga en sí lo hace `EmojiDataLoader`).
- **`GlobalSearch`**: las `OperationCanceledException` en el loop de deferred sources se capturan sin loguear. La clase no tiene `ILogger`.

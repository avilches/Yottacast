# Logging

Configurado con Serilog en `BuildServices()`. Los logs se escriben en fichero rotatorio diario (retención de 7 días):

- macOS: `~/Library/Logs/Yottacast/yottacast-<fecha>.log`
- Windows/Linux: `%LOCALAPPDATA%\Yottacast\Logs\yottacast-<fecha>.log`

El nivel mínimo es `Debug`. Todos los servicios reciben `ILogger<T>` por inyección.

## Output template

Ambos destinos (fichero y consola del CLI) usan la misma estructura de campos:

```
{Timestamp:HH:mm:ss.fff} [{Level:u5}] [{SourceContext}] {Message:lj}{NewLine}{Exception}
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

# Logging

Configurado con Serilog en `BuildServices()`. Los logs se escriben en fichero rotatorio diario (retención de 7 días):

- macOS: `~/Library/Logs/Yottacast/yottacast-<fecha>.log`
- Windows/Linux: `%LOCALAPPDATA%\Yottacast\Logs\yottacast-<fecha>.log`

El nivel mínimo es `Debug`. Todos los servicios reciben `ILogger<T>` por inyección.

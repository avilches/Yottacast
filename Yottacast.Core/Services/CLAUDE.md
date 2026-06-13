## Tests

Al modificar esta area, actualizar los tests en `Yottacast.Core.Tests/Services/`:
- `UserSettingsTests.cs` — persistencia, migraciones y auto-reparacion de settings
- `BrowserTerminalDiscoveryTests.cs` — descubrimiento de navegadores y terminales instalados
- `ProcessRunnerTests.cs` — ejecucion de procesos y escaping de comandos
- `HistoryServiceTests.cs` — historial de búsquedas
- `AppIconCacheTests.cs` — dedup de cargas en vuelo y limpieza de iconos huerfanos
- `FileIconCacheTests.cs` — reintento tras fallo de disco e invalidacion de todas las versiones
- `PluginServiceTests.cs` — thread-safety del diccionario de iconos durante recarga
